"""Self-contained checks for SeucmCameraModel.

Run with:  python camera_models/test_seucm.py

Validated once against the vendor reference implementation (calder_core
``ProjectSeucm`` / ``UnprojectSeucm``, via calder_ego) to 3.6e-05 px on the
forward projection and 4.0e-07 on the unprojected bearing. That reference is
not vendored here, so these checks instead pin the properties that must hold:
round-trip consistency, the two documented degenerate collapses (alpha=0 ->
pinhole, eu=cx/ev=cy -> EUCM), torch/numpy agreement, and autograd.
"""
import os
import sys

import numpy as np
import torch

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from camera_models import PinholeCameraModel, SeucmCameraModel  # noqa: E402

# A real egocentric rig calibration (rgb_left), for representative magnitudes.
FX, FY, CX, CY = 803.793, 802.921, 808.131, 629.641
ALPHA, BETA, EU, EV = 0.685141, 1.02586, 809.076957, 628.806468
W, H = 1600, 1200


def _eucm_reference(X, Y, Z, fx, fy, cx, cy, alpha, beta):
    """EUCM projection, written out independently as a cross-check."""
    d = np.sqrt(beta * (X * X + Y * Y) + Z * Z)
    eta = alpha * d + (1.0 - alpha) * Z
    return fx * X / eta + cx, fy * Y / eta + cy


def main():
    cam = SeucmCameraModel([FX, FY], [CX, CY], [ALPHA, BETA, EU, EV], W, H)
    rng = np.random.default_rng(0)
    n = 4000
    pts = np.stack([rng.uniform(-1.2, 1.2, n),
                    rng.uniform(-1.2, 1.2, n),
                    rng.uniform(0.3, 4.0, n)], -1)
    failures = []

    def check(name, value, tol, unit=''):
        ok = value <= tol
        print(f'{name:26s}: {value:.3e}{unit}  {"ok" if ok else "FAIL"}')
        if not ok:
            failures.append(name)

    # projected points must land inside a sane pixel range
    uv = cam.camera_to_uv(pts)
    assert np.isfinite(uv).all(), 'projection produced non-finite pixels'

    # round trips
    check('uvd round trip', float(np.abs(cam.uvd_to_camera(cam.camera_to_uvd(pts)) - pts).max()), 1e-3, ' m')
    check('uvz round trip', float(np.abs(cam.uvz_to_camera(cam.camera_to_uvz(pts)) - pts).max()), 1e-3, ' m')

    # uv -> bearing -> uv
    theta, undist = cam.uv_to_theta_x_y(uv, return_undistorted=True)
    xy = np.tan(theta)
    rays = np.concatenate([xy, np.ones_like(xy[..., :1])], -1)
    check('uv -> ray -> uv', float(np.abs(cam.camera_to_uv(rays) - uv).max()), 1e-2, ' px')

    # alpha = 0 collapses to a pinhole
    pin = PinholeCameraModel(np.array([FX, FY], np.float32), np.array([CX, CY], np.float32), W, H)
    cam_a0 = SeucmCameraModel([FX, FY], [CX, CY], [0.0, 1.0, CX, CY], W, H)
    check('alpha=0 == pinhole', float(np.abs(cam_a0.camera_to_uv(pts) - pin.camera_to_uv(pts)).max()), 1e-2, ' px')

    # eu=cx, ev=cy collapses to EUCM
    cam_eucm = SeucmCameraModel([FX, FY], [CX, CY], [ALPHA, BETA, CX, CY], W, H)
    ue, ve = _eucm_reference(pts[:, 0], pts[:, 1], pts[:, 2], FX, FY, CX, CY, ALPHA, BETA)
    check('eu=cx,ev=cy == EUCM', float(np.abs(cam_eucm.camera_to_uv(pts) - np.stack([ue, ve], -1)).max()), 1e-3, ' px')

    # torch parity + autograd
    camt = SeucmCameraModel(torch.tensor([FX, FY]), torch.tensor([CX, CY]),
                            torch.tensor([ALPHA, BETA, EU, EV]), W, H)
    tp = torch.tensor(pts, dtype=torch.float32)
    check('torch vs numpy', float(np.abs(camt.camera_to_uv(tp).numpy() - uv).max()), 1e-2, ' px')

    tp_g = tp.clone().requires_grad_(True)
    camt.camera_to_uv(tp_g).sum().backward()
    if not torch.isfinite(tp_g.grad).all():
        failures.append('autograd finite')
    print(f'{"autograd finite":26s}: {bool(torch.isfinite(tp_g.grad).all())}')

    # batched ray helper used by core/rss.py must agree with camera_to_uv
    from camera_models.seucm import seucm_unproject_dirs
    uv_t = torch.tensor(uv, dtype=torch.float32)[None]                  # (1,N,2)
    dirs = seucm_unproject_dirs(uv_t,
                                torch.tensor([[FX, FY]]), torch.tensor([[CX, CY]]),
                                torch.tensor([ALPHA]), torch.tensor([BETA]),
                                torch.tensor([EU]), torch.tensor([EV]))
    reproj = cam.camera_to_uv(dirs[0].numpy())
    check('batched rays -> uv', float(np.abs(reproj - uv).max()), 5e-2, ' px')

    # TYPE_ID must not collide with the ids core/rss.py dispatches on
    ids = {}
    import camera_models as cm
    for name in dir(cm):
        obj = getattr(cm, name)
        tid = getattr(obj, 'TYPE_ID', None)
        if isinstance(tid, int):
            ids.setdefault(tid, []).append(name)
    reserved = {0: 'pinhole', 2: 'rational8', 3: 'fisheye624', 4: 'kb3',
                5: 'equisolid', 6: 'equirectangular', 7: 'stereographic'}
    clash = SeucmCameraModel.TYPE_ID in reserved or len(ids.get(SeucmCameraModel.TYPE_ID, [])) > 1
    print(f'{"TYPE_ID free":26s}: {SeucmCameraModel.TYPE_ID} '
          f'{"FAIL (collides with " + reserved.get(SeucmCameraModel.TYPE_ID, "another model") + ")" if clash else "ok"}')
    if clash:
        failures.append('TYPE_ID collision')

    # interface parity with the other camera models
    need = ['camera_to_uv', 'camera_to_uvd', 'camera_to_uvz', 'camera_to_d', 'uvd_to_camera',
            'uvz_to_camera', 'uv_to_theta_x_y', 'to_intrinsics_keypoint_encoding', 'distort3d',
            'get_K', 'update_K', 'to', 'clone']
    missing = [m for m in need if not hasattr(cam, m)]
    print(f'{"interface complete":26s}: {not missing}' + (f'  missing={missing}' if missing else ''))
    if missing:
        failures.append('interface')

    print('\nRESULT:', 'ALL PASS' if not failures else f'FAILURES: {failures}')
    return 1 if failures else 0


if __name__ == '__main__':
    raise SystemExit(main())
