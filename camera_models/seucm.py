from typing import Sequence

import numpy as np
import torch


class SeucmDistortion:
    """SEUCM distortion expressed in normalised pinhole coordinates.

    The camera model itself projects with the vendor equations (see
    :class:`SeucmCameraModel`). This wrapper exposes that mapping in the
    ``evaluate`` / ``inverse_evaluate`` form the rest of the repo expects:

        evaluate:         (x, y) = (X/Z, Y/Z)  ->  ((u - cx)/fx, (v - cy)/fy)
        inverse_evaluate: ((u - cx)/fx, (v - cy)/fy)  ->  (X/Z, Y/Z)

    so that callers can keep using the shared ``uv = f * xy + c`` convention.
    """

    def __init__(self, params, f, c):
        self.params = params
        self.f = f
        self.c = c
        nonzero = torch.count_nonzero if isinstance(params, torch.Tensor) else np.count_nonzero
        # alpha == 0 collapses SEUCM to a plain pinhole
        self.is_distorted = bool(nonzero(params[:1]))

    def _intr(self):
        alpha, beta, eu, ev = (self.params[i] for i in range(4))
        fx, fy = self.f[0], self.f[1]
        cx, cy = self.c[0], self.c[1]
        return fx, fy, cx, cy, alpha, beta, eu, ev

    def evaluate(self, xy):
        lib = torch if isinstance(xy, torch.Tensor) else np
        fx, fy, cx, cy, alpha, beta, eu, ev = self._intr()

        x, y = xy[..., 0], xy[..., 1]
        ones = lib.ones_like(x)
        u, v = _project(x, y, ones, fx, fy, cx, cy, alpha, beta, eu, ev, lib)
        return lib.stack([(u - cx) / fx, (v - cy) / fy], axis=-1)

    def inverse_evaluate(self, xy_dist, *args, **kwargs):
        lib = torch if isinstance(xy_dist, torch.Tensor) else np
        fx, fy, cx, cy, alpha, beta, eu, ev = self._intr()

        u = xy_dist[..., 0] * fx + cx
        v = xy_dist[..., 1] * fy + cy
        bx, by, bz = _unproject(u, v, fx, fy, cx, cy, alpha, beta, eu, ev, lib)
        bz = _safe(bz, lib)
        return lib.stack([bx / bz, by / bz], axis=-1)


def _project(X, Y, Z, fx, fy, cx, cy, alpha, beta, eu, ev, lib):
    """Forward SEUCM projection (vendor equations).

        e_u = (eu - cx) / fx,   e_v = (ev - cy) / fy
        X_e = X - e_u * Z,      Y_e = Y - e_v * Z
        D_e = sqrt(beta * (X_e^2 + Y_e^2) + Z^2)
        Z_e = alpha * D_e + (1 - alpha) * Z
        u   = fx * X_e / Z_e + eu
        v   = fy * Y_e / Z_e + ev
    """
    e_u = (eu - cx) / fx
    e_v = (ev - cy) / fy
    X_e = X - e_u * Z
    Y_e = Y - e_v * Z
    D_e = lib.sqrt(beta * (X_e * X_e + Y_e * Y_e) + Z * Z)
    Z_e = alpha * D_e + (1.0 - alpha) * Z
    Z_e = _safe(Z_e, lib)
    return fx * X_e / Z_e + eu, fy * Y_e / Z_e + ev


def _unproject(u, v, fx, fy, cx, cy, alpha, beta, eu, ev, lib):
    """Closed-form SEUCM unprojection, returning an un-normalised bearing.

        mx = (u - eu) / fx,   my = (v - ev) / fy,   r2 = mx^2 + my^2
        mz = (1 - alpha^2 * beta * r2) / ((1 - alpha) + alpha * sqrt(disc))
        with disc = 1 - (2*alpha - 1) * beta * r2
        bearing = (mx + e_u * mz,  my + e_v * mz,  mz)
    """
    e_u = (eu - cx) / fx
    e_v = (ev - cy) / fy
    mx = (u - eu) / fx
    my = (v - ev) / fy
    r2 = mx * mx + my * my

    disc = 1.0 - (2.0 * alpha - 1.0) * beta * r2
    zero = disc - disc  # 0 with the right type/device
    disc = lib.where(disc >= 0, disc, zero)
    denom = (1.0 - alpha) + alpha * lib.sqrt(disc)
    denom = _safe(denom, lib)

    mz = (1.0 - alpha * alpha * beta * r2) / denom
    return mx + e_u * mz, my + e_v * mz, mz


def _safe(x, lib, eps=1e-12):
    """Clamp away from zero, keeping sign, so divisions stay finite."""
    if lib is torch:
        return torch.where(x.abs() < eps, torch.full_like(x, eps), x)
    x = np.asarray(x)
    return np.where(np.abs(x) < eps, eps, x)


class SeucmCameraModel:
    """Special Enhanced Unified Camera Model (SEUCM).

    A unit-sphere + ellipsoid projection (EUCM) extended with a distortion
    centre ``(eu, ev)`` that is separate from the principal point ``(cx, cy)``.
    Used by egocentric rigs whose lens is not coaxial with the sensor; it is
    *not* representable by fisheye624/rational8, so those models can only
    approximate it -- badly at large radius.

    Reduces bit-exactly to EUCM when ``eu == cx`` and ``ev == cy``, and to a
    pinhole when ``alpha == 0``.

    f:      (fx, fy)
    c:      (cx, cy)               principal point
    params: (alpha, beta, eu, ev)  alpha/beta are the ellipsoid shape
                                   parameters, (eu, ev) the distortion centre
    """

    TYPE = "seucm"
    TYPE_ID = 5

    def __init__(self, f, c, params: Sequence[float], width: int, height: int):
        assert len(f) == 2, "Focal length must be a 2D vector (fx, fy)"
        assert len(c) == 2, "Principal point must be a 2D vector (cx, cy)"
        assert len(params) == 4, "SEUCM parameters must be a 4D vector (alpha, beta, eu, ev)"

        if isinstance(f, torch.Tensor):
            self.f = f.float()
            self.c = c.float()
            self.params = params.float()
        else:
            self.f = np.array(f, dtype=np.float32)
            self.c = np.array(c, dtype=np.float32)
            self.params = np.array(params, dtype=np.float32)

        self.width = width
        self.height = height
        self.distortion_model = SeucmDistortion(self.params, self.f, self.c)

    def _intr(self):
        alpha, beta, eu, ev = (self.params[i] for i in range(4))
        return self.f[0], self.f[1], self.c[0], self.c[1], alpha, beta, eu, ev

    def camera_to_uv(self, v):
        lib = torch if isinstance(v, torch.Tensor) else np
        u_, v_ = _project(v[..., 0], v[..., 1], v[..., 2], *self._intr(), lib)
        return lib.stack([u_, v_], axis=-1)

    def camera_to_d(self, p):
        lib = torch if isinstance(p, torch.Tensor) else np
        r3 = lib.linalg.norm(p, axis=-1)
        return r3 * lib.sign(p[..., 2])

    def camera_to_uvd(self, v):
        lib = torch if isinstance(v, torch.Tensor) else np
        uv = self.camera_to_uv(v)
        d = self.camera_to_d(v)
        return lib.concatenate([uv, d[..., None]], axis=-1)

    def uvd_to_camera(self, p):
        """(u, v, d) -> 3D camera point, where d = ||p|| * sign(Z)."""
        assert p.shape[-1] == 3, "Input must have 3 components (u, v, d)"
        lib = torch if isinstance(p, torch.Tensor) else np

        u, v, d = p[..., 0], p[..., 1], p[..., 2]
        bx, by, bz = _unproject(u, v, *self._intr(), lib)

        dir_3d = lib.stack([bx, by, bz], axis=-1)
        norm_dir = _safe(lib.linalg.norm(dir_3d, axis=-1), lib)
        scale = lib.abs(d) / norm_dir

        pt = dir_3d * scale[..., None]
        sign_z = lib.sign(d)
        return lib.stack([pt[..., 0], pt[..., 1], pt[..., 2] * sign_z], axis=-1)

    def uv_to_theta_x_y(self, uv, return_undistorted=False):
        lib = torch if isinstance(uv, torch.Tensor) else np
        bx, by, bz = _unproject(uv[..., 0], uv[..., 1], *self._intr(), lib)
        bz = _safe(bz, lib)
        xy_undist = lib.stack([bx / bz, by / bz], axis=-1)

        theta = torch.atan(xy_undist) if lib is torch else np.arctan(xy_undist)
        if return_undistorted:
            return theta, (self.f * xy_undist + self.c)
        return theta

    def to_intrinsics_keypoint_encoding(self, keypoints, return_undistorted=False):
        return self.uv_to_theta_x_y(keypoints, return_undistorted)

    def distort3d(self, v):
        """Return the 3D point whose *pinhole* projection equals this model's."""
        lib = torch if isinstance(v, torch.Tensor) else np
        Z = v[..., 2]
        Z_safe = _safe(Z, lib)
        xy = lib.stack([v[..., 0] / Z_safe, v[..., 1] / Z_safe], axis=-1)
        xy_dist = self.distortion_model.evaluate(xy)
        return lib.stack([xy_dist[..., 0] * Z, xy_dist[..., 1] * Z, Z], axis=-1)

    def camera_to_uvz(self, v):
        lib = torch if isinstance(v, torch.Tensor) else np
        uv = self.camera_to_uv(v)
        return lib.concatenate([uv, v[..., 2:3]], axis=-1)

    def uvz_to_camera(self, p):
        assert p.shape[-1] == 3
        lib = torch if isinstance(p, torch.Tensor) else np

        bx, by, bz = _unproject(p[..., 0], p[..., 1], *self._intr(), lib)
        bz = _safe(bz, lib)
        z = p[..., 2]
        return lib.stack([bx / bz * z, by / bz * z, z], axis=-1)

    def get_K(self):
        fx, fy = self.f[0], self.f[1]
        cx, cy = self.c[0], self.c[1]
        return np.array([[fx, 0, cx],
                         [0, fy, cy],
                         [0, 0, 1]], dtype=np.float32)

    def update_K(self, K, width=None, height=None):
        self.f[0] = K[0, 0]
        self.f[1] = K[1, 1]
        self.c[0] = K[0, 2]
        self.c[1] = K[1, 2]
        if width is not None and height is not None:
            self.width = width
            self.height = height
        # the distortion centre is tied to the intrinsics, so rebuild
        self.distortion_model = SeucmDistortion(self.params, self.f, self.c)
        return self

    def to(self, device):
        if isinstance(self.f, torch.Tensor):
            self.f = self.f.to(device)
            self.c = self.c.to(device)
            self.params = self.params.to(device)
            self.distortion_model = SeucmDistortion(self.params, self.f, self.c)
        return self

    def clone(self):
        if isinstance(self.f, torch.Tensor):
            return SeucmCameraModel(self.f.clone(), self.c.clone(), self.params.clone(),
                                    self.width, self.height)
        return SeucmCameraModel(self.f.copy(), self.c.copy(), self.params.copy(),
                                self.width, self.height)
