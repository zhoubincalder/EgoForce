"""Stereo triangulation for metric hand placement.

The monocular pipeline recovers a root-relative pose from one crop and then
solves for the translation from ray geometry alone (see ``core/rss.py``). That
solve is what carries the depth/scale ambiguity: it is well posed only when the
keypoint rays span enough angular extent, and it degrades as the hand gets
smaller in frame.

When a session has a calibrated stereo pair, depth does not have to be inferred
-- it can be measured. These helpers triangulate corresponding 2D keypoints from
two views into metric 3D, and report a per-point residual so callers can fall
back to the monocular solve when a correspondence is unreliable.

Nothing here assumes a particular lens: bearings come from the camera model's
own unprojection, so pinhole, fisheye624, rational8 and SEUCM all work.
"""
import numpy as np
import torch


def _lib(x):
    return torch if isinstance(x, torch.Tensor) else np


def bearings_from_uv(camera_model, uv):
    """Unit viewing rays for pixel coordinates ``uv`` (..., 2).

    Uses the camera model's own unprojection, so this is exact for whatever
    lens the session was calibrated with -- not a pinhole approximation.
    """
    lib = _lib(uv)
    theta = camera_model.uv_to_theta_x_y(uv)
    xy = torch.tan(theta) if lib is torch else np.tan(theta)
    ones = lib.ones_like(xy[..., :1])
    d = lib.concatenate([xy, ones], axis=-1) if lib is np else torch.cat([xy, ones], dim=-1)
    n = lib.linalg.norm(d, axis=-1, keepdims=True) if lib is np else \
        torch.linalg.norm(d, dim=-1, keepdim=True)
    return d / (n + 1e-12)


def triangulate_rays(o_a, d_a, o_b, d_b):
    """Mid-point triangulation of two rays, expressed in a common frame.

    o_*: (..., 3) ray origins;  d_*: (..., 3) unit directions.

    Returns ``(points, residual)`` where ``points`` is the mid-point of the
    common perpendicular and ``residual`` is the distance between the two
    closest-approach points -- small when the rays genuinely intersect, large
    when the correspondence is wrong or the geometry is degenerate.
    """
    lib = _lib(d_a)
    r = o_b - o_a
    daa = (d_a * d_a).sum(-1)
    dbb = (d_b * d_b).sum(-1)
    dab = (d_a * d_b).sum(-1)
    rda = (r * d_a).sum(-1)
    rdb = (r * d_b).sum(-1)

    denom = daa * dbb - dab * dab           # ~0 when the rays are parallel
    denom = lib.where(lib.abs(denom) < 1e-12,
                      lib.full_like(denom, 1e-12) if lib is torch else np.full_like(denom, 1e-12),
                      denom)

    s = (rda * dbb - dab * rdb) / denom     # along ray A
    t = (rda * dab - daa * rdb) / denom     # along ray B

    pa = o_a + d_a * s[..., None]
    pb = o_b + d_b * t[..., None]
    residual = lib.linalg.norm(pa - pb, axis=-1) if lib is np else \
        torch.linalg.norm(pa - pb, dim=-1)
    return 0.5 * (pa + pb), residual


def triangulate_stereo(cam_a, cam_b, uv_a, uv_b, R_ab, t_ab):
    """Triangulate matched pixels from two calibrated views.

    cam_a, cam_b : camera models for view A and view B
    uv_a, uv_b   : (..., 2) corresponding pixel coordinates
    R_ab, t_ab   : pose of camera B in camera A's frame, i.e.
                   ``p_A = R_ab @ p_B + t_ab``. For a stereo rig these come
                   from ``rig_T_sensor`` of each eye:
                   ``A_T_B = rig_T_A^-1 @ rig_T_B``.

    Returns ``(points_in_A, residual)``. ``points_in_A`` is metric -- the scale
    comes from the baseline in ``t_ab``, not from the network.
    """
    lib = _lib(uv_a)
    d_a = bearings_from_uv(cam_a, uv_a)
    d_b_local = bearings_from_uv(cam_b, uv_b)

    # rotate B's rays into A's frame
    if lib is torch:
        d_b = (R_ab @ d_b_local.unsqueeze(-1)).squeeze(-1)
        o_a = torch.zeros_like(d_a)
        o_b = t_ab.expand_as(d_a)
    else:
        d_b = d_b_local @ np.asarray(R_ab).T
        o_a = np.zeros_like(d_a)
        o_b = np.broadcast_to(np.asarray(t_ab, dtype=d_a.dtype), d_a.shape)

    return triangulate_rays(o_a, d_a, o_b, d_b)


def stereo_translation(cam_a, cam_b, uv_a, uv_b, R_ab, t_ab, j3d_root_relative,
                       weights=None, max_residual=0.02):
    """Metric translation placing a root-relative pose using stereo depth.

    Drop-in alternative to the ray-space translation solve when both views see
    the limb: triangulate the corresponding keypoints, then take the (weighted)
    offset between the triangulated points and the root-relative prediction.

    Correspondences whose triangulation residual exceeds ``max_residual``
    (metres) are dropped -- that is the signal that the match is bad or the rays
    are near-parallel. Returns ``(translation, n_used)``; ``n_used == 0`` means
    stereo could not be trusted and the caller should keep the monocular solve.
    """
    pts, residual = triangulate_stereo(cam_a, cam_b, uv_a, uv_b, R_ab, t_ab)
    lib = _lib(pts)

    good = (residual < max_residual) & (pts[..., 2] > 0)
    if weights is not None:
        good = good & (weights > 0)

    n_used = int(good.sum())
    if n_used == 0:
        zeros = lib.zeros(3, dtype=pts.dtype) if lib is np else \
            torch.zeros(3, dtype=pts.dtype, device=pts.device)
        return zeros, 0

    offset = pts - j3d_root_relative
    sel = offset[good]
    if weights is not None:
        w = weights[good][..., None]
        transl = (sel * w).sum(0) / w.sum().clip(1e-8) if lib is np else \
            (sel * w).sum(0) / w.sum().clamp_min(1e-8)
    else:
        transl = sel.mean(0)
    return transl, n_used
