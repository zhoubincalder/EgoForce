"""Checks for the bounded stable-box fallback (CPU only, no model needed).

Run with:  python demo/test_box_freeze.py

Reproduces the failure the bounds exist to prevent: a hand drifting slowly
enough that consecutive detections always overlap above ``hand_stable_iou``,
so the unbounded IoU test holds the same crop box forever while the hand walks
away from it.
"""
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from demo_utils import compute_bbox_iou, should_reuse_previous_box  # noqa: E402

IOU_THR = 0.80
MAX_DRIFT = 12.0
MAX_FRAMES = 15
SIDE = 260.0          # typical hand box size in these clips


def simulate(px_per_frame, n=120, max_drift=MAX_DRIFT, max_frames=MAX_FRAMES):
    """Slide a box at a constant rate; return the crop-box vs detection drift."""
    held_box = np.array([700.0, 800.0, 700.0 + SIDE, 800.0 + SIDE])
    held = 0
    drifts = []
    for i in range(n):
        det = np.array([700.0 + i * px_per_frame, 800.0,
                        700.0 + i * px_per_frame + SIDE, 800.0 + SIDE])
        iou = compute_bbox_iou(det, held_box)
        if should_reuse_previous_box(held_box, det, iou, IOU_THR, held, max_drift, max_frames):
            held += 1
        else:
            held_box = det.copy()
            held = 0
        drifts.append(abs((held_box[0] + held_box[2]) / 2 - (det[0] + det[2]) / 2))
    return np.array(drifts)


def main():
    failures = []

    # 1. Slow drift: the exact regime that used to freeze forever.
    #    1 px/frame keeps IoU at ~0.99, so the unbounded rule never releases.
    d_bounded = simulate(1.0)
    d_unbounded = simulate(1.0, max_drift=None, max_frames=None)
    print(f'slow drift (1 px/frame), 120 frames')
    print(f'  unbounded : max drift = {d_unbounded.max():6.1f} px   <- the bug')
    print(f'  bounded   : max drift = {d_bounded.max():6.1f} px')
    if d_unbounded.max() <= MAX_DRIFT:
        failures.append('unbounded rule failed to reproduce the drift')
    if d_bounded.max() > MAX_DRIFT + 1e-6:
        failures.append(f'bounded drift {d_bounded.max():.1f} exceeds cap {MAX_DRIFT}')

    # 2. A stationary hand must still be fully stabilised (no jitter reintroduced).
    d_still = simulate(0.0)
    print(f'stationary hand      : max drift = {d_still.max():6.1f} px (expect 0.0)')
    if d_still.max() > 1e-6:
        failures.append('stationary hand should never drift')

    # 3. Fast motion drops IoU below the threshold, so reuse never applies.
    fast = np.array([700.0, 800.0, 700.0 + SIDE, 800.0 + SIDE])
    far = fast + np.array([SIDE, 0, SIDE, 0])       # zero overlap
    reuse = should_reuse_previous_box(fast, far, compute_bbox_iou(far, fast),
                                      IOU_THR, 0, MAX_DRIFT, MAX_FRAMES)
    print(f'non-overlapping box  : reuse = {reuse} (expect False)')
    if reuse:
        failures.append('must not reuse a non-overlapping box')

    # 4. The frame cap alone bounds reuse even when drift stays tiny.
    held_box = np.array([700.0, 800.0, 700.0 + SIDE, 800.0 + SIDE])
    held, releases = 0, 0
    for _ in range(100):
        det = held_box + np.array([0.05, 0, 0.05, 0])   # drift far below the px cap
        if should_reuse_previous_box(held_box, det, compute_bbox_iou(det, held_box),
                                     IOU_THR, held, MAX_DRIFT, MAX_FRAMES):
            held += 1
        else:
            held_box, held = det.copy(), 0
            releases += 1
    print(f'tiny drift, 100 frames: released {releases}x via frame cap (expect >= 6)')
    if releases < 6:
        failures.append('frame cap did not bound reuse')

    print('\nRESULT:', 'ALL PASS' if not failures else f'FAILURES: {failures}')
    return 1 if failures else 0


if __name__ == '__main__':
    raise SystemExit(main())
