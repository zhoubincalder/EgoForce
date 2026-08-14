"""Checks for stereo triangulation (CPU only, no model or GPU needed).

Run with:  python core/test_stereo.py

Ground truth is synthetic: known 3D points are projected through two calibrated
cameras, then triangulated back. Recovery to sub-millimetre proves the geometry,
the SEUCM unprojection and the frame conventions all line up.
"""
import os
import sys

import numpy as np
import torch

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from camera_models import PinholeCameraModel, SeucmCameraModel  # noqa: E402
from core.stereo import triangulate_stereo, stereo_translation  # noqa: E402

# real rig: rgb_left / rgb_right, 64.6 mm baseline
FX, FY, CX, CY = 803.793, 802.921, 808.131, 629.641
ALPHA, BETA, EU, EV = 0.685141, 1.02586, 809.076957, 628.806468
W, H = 1600, 1200
BASELINE = 0.0646


def make_cams(kind):
    if kind == 'seucm':
        mk = lambda: SeucmCameraModel([FX, FY], [CX, CY], [ALPHA, BETA, EU, EV], W, H)
    else:
        mk = lambda: PinholeCameraModel(np.array([FX, FY], np.float32),
                                        np.array([CX, CY], np.float32), W, H)
    return mk(), mk()


def main():
    failures = []
    rng = np.random.default_rng(0)

    def check(name, value, tol, unit=''):
        ok = value <= tol
        print(f'{name:34s}: {value:.3e}{unit}  {"ok" if ok else "FAIL"}')
        if not ok:
            failures.append(name)

    # Points in front of camera A, at plausible hand distances.
    n = 500
    pts_a = np.stack([rng.uniform(-0.25, 0.25, n),
                      rng.uniform(-0.25, 0.25, n),
                      rng.uniform(0.25, 1.20, n)], -1)

    # Camera B sits to the right by the baseline, no rotation.
    R_ab = np.eye(3)
    t_ab = np.array([BASELINE, 0.0, 0.0])

    for kind in ('pinhole', 'seucm'):
        cam_a, cam_b = make_cams(kind)
        pts_b = (pts_a - t_ab) @ R_ab            # express the same points in B's frame
        uv_a = cam_a.camera_to_uv(pts_a)
        uv_b = cam_b.camera_to_uv(pts_b)

        rec, residual = triangulate_stereo(cam_a, cam_b, uv_a, uv_b, R_ab, t_ab)
        check(f'{kind}: recover 3D', float(np.abs(rec - pts_a).max()), 1e-3, ' m')
        check(f'{kind}: residual', float(residual.max()), 1e-4, ' m')

    # A rotated second view must also work (rig eyes are rarely perfectly parallel).
    cam_a, cam_b = make_cams('seucm')
    ang = np.deg2rad(3.0)
    R_ab = np.array([[np.cos(ang), 0, np.sin(ang)], [0, 1, 0], [-np.sin(ang), 0, np.cos(ang)]])
    t_ab = np.array([BASELINE, 0.002, -0.001])
    pts_b = (pts_a - t_ab) @ R_ab
    uv_a = cam_a.camera_to_uv(pts_a)
    uv_b = cam_b.camera_to_uv(pts_b)
    rec, residual = triangulate_stereo(cam_a, cam_b, uv_a, uv_b, R_ab, t_ab)
    check('seucm+rotation: recover 3D', float(np.abs(rec - pts_a).max()), 1e-3, ' m')

    # Depth accuracy should degrade gracefully with pixel noise, not blow up.
    noise_px = 1.0
    uv_a_n = uv_a + rng.normal(0, noise_px, uv_a.shape)
    uv_b_n = uv_b + rng.normal(0, noise_px, uv_b.shape)
    rec_n, res_n = triangulate_stereo(cam_a, cam_b, uv_a_n, uv_b_n, R_ab, t_ab)
    z_err = np.abs(rec_n[:, 2] - pts_a[:, 2])
    print(f'{"1px noise -> depth err":34s}: median={np.median(z_err)*1000:.1f} mm  '
          f'p90={np.percentile(z_err,90)*1000:.1f} mm')
    if np.median(z_err) > 0.05:
        failures.append('depth error under 1px noise')

    # stereo_translation should place a root-relative pose metrically.
    root = pts_a[0]
    j3d_rel = pts_a - root                       # root-relative "prediction"
    transl, used = stereo_translation(cam_a, cam_b, uv_a, uv_b, R_ab, t_ab, j3d_rel)
    check('stereo_translation', float(np.abs(transl - root).max()), 1e-3, ' m')
    print(f'{"correspondences used":34s}: {used}/{n}')
    if used == 0:
        failures.append('stereo_translation used no points')

    # Bad correspondences must be rejected, not silently trusted.
    uv_b_bad = uv_b[::-1].copy()                 # shuffled -> wrong matches
    _, used_bad = stereo_translation(cam_a, cam_b, uv_a, uv_b_bad, R_ab, t_ab, j3d_rel)
    print(f'{"scrambled matches used":34s}: {used_bad}/{n} (expect << {n})')
    if used_bad > n * 0.25:
        failures.append('failed to reject scrambled correspondences')

    # torch path must agree with numpy
    cam_at = SeucmCameraModel(torch.tensor([FX, FY]), torch.tensor([CX, CY]),
                              torch.tensor([ALPHA, BETA, EU, EV]), W, H)
    rec_t, _ = triangulate_stereo(cam_at, cam_at,
                                  torch.tensor(uv_a, dtype=torch.float32),
                                  torch.tensor(uv_b, dtype=torch.float32),
                                  torch.tensor(R_ab, dtype=torch.float32),
                                  torch.tensor(t_ab, dtype=torch.float32))
    check('torch vs numpy', float(np.abs(rec_t.numpy() - rec).max()), 1e-2, ' m')

    print('\nRESULT:', 'ALL PASS' if not failures else f'FAILURES: {failures}')
    return 1 if failures else 0


if __name__ == '__main__':
    raise SystemExit(main())
