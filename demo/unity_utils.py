import time
import cv2
import zmq
import msgpack
import numpy as np
import threading
import json


class FPS:
    def __init__(self, prefix):
        self.prefix = prefix
        self.tic = None
        self.fps = None
        self.upd_rate = 0.001

    def __call__(self):
        if self.tic is None:
            self.tic = time.time()
            return

        toc = time.time()
        dt = toc - self.tic
        self.tic = toc
        if dt > 0:
            if self.fps is None:
                self.fps = 1 / dt
            else:
                self.fps = self.fps + self.upd_rate * (1 / dt - self.fps)
        
        if self.fps is not None:
            print(self.prefix, round(self.fps, 2), ' fps')


def serialise(v):
    if isinstance(v, np.ndarray) and v.dtype == np.float64:
        v = v.astype(np.float32)

    if isinstance(v, np.ndarray) and len(v.shape) == 3:
        return v.tobytes()
    
    if isinstance(v, np.ndarray):
        return v.tolist()
    
    for t in [int, float, str]:
        if isinstance(v, t):
            return v 

    for t in [list, tuple]:
        if isinstance(v, t):
            return [serialise(x) for x in v]   

    if isinstance(v, dict):
        return {k: serialise(val) for k, val in v.items()}

    raise ValueError(f'Unsupported type: {type(v)}')


class HandVisibilityTracker:
    def __init__(self, miss_threshold):
        self.miss_threshold = max(0, int(miss_threshold))
        self._miss_counts = np.zeros(2, dtype=np.int32)
        self._last_visible_meshes = [None, None]

    def _normalise_visible(self, detected_visible):
        detected_visible = np.asarray(detected_visible, dtype=bool).reshape(-1)
        if detected_visible.shape[0] != 2:
            raise ValueError(
                f"Expected visibility for exactly 2 hands, got shape {detected_visible.shape}"
            )
        return detected_visible

    def update(self, detected_visible):
        detected_visible = self._normalise_visible(detected_visible)
        self._miss_counts = np.where(detected_visible, 0, self._miss_counts + 1)
        has_cached_mesh = np.array(
            [mesh is not None for mesh in self._last_visible_meshes],
            dtype=bool,
        )
        return np.logical_or(
            detected_visible,
            np.logical_and(has_cached_mesh, self._miss_counts < self.miss_threshold),
        )

    def resolve_mesh_buffers(self, detected_visible, limb_meshes):
        detected_visible = self._normalise_visible(detected_visible)
        self._miss_counts = np.where(detected_visible, 0, self._miss_counts + 1)

        if len(limb_meshes) != 2:
            raise ValueError(f"Expected exactly 2 limb meshes, got {len(limb_meshes)}")

        resolved_meshes = []
        resolved_visible = np.zeros(2, dtype=bool)

        for hand_index, limb_mesh in enumerate(limb_meshes):
            current_mesh = np.ascontiguousarray(limb_mesh, dtype=np.float32)

            if detected_visible[hand_index]:
                self._last_visible_meshes[hand_index] = current_mesh.copy()
                resolved_meshes.append(current_mesh)
                resolved_visible[hand_index] = True
                continue

            cached_mesh = self._last_visible_meshes[hand_index]
            if cached_mesh is not None:
                resolved_meshes.append(cached_mesh)
                resolved_visible[hand_index] = self._miss_counts[hand_index] < self.miss_threshold
            else:
                resolved_meshes.append(current_mesh)
                resolved_visible[hand_index] = False

        return resolved_meshes, resolved_visible



class SendToUnity:
    def __init__(self):
        ctx = zmq.Context.instance()
        self.socket = ctx.socket(zmq.PUSH)
        self.socket.bind("tcp://*:5555")
        self.socket.set_hwm(1)
        self.socket.setsockopt(zmq.LINGER, 0)
        self.fps = FPS("Send:")
        self.lock = threading.Lock()

    def _send(self, meta: dict, data: bytes):
        with self.lock:
            self.socket.send_json(meta, zmq.SNDMORE)
            self.socket.send(data)
            self.fps()

    def send_json(self, obj):
        b = json.dumps(obj).encode("utf-8")
        self._send({"type":"json","length":len(b)}, b)

    def send_bytes(self, b):
        self._send({"type":"bytes","length":len(b)}, b)

    def send_image(self, img, frame_index):
        _, buf = cv2.imencode(".jpg", img, [cv2.IMWRITE_JPEG_QUALITY, 80])
        self._send(
            {"type":"image", 
             "format":"jpg", 
             "shape":img.shape,
             "index": frame_index
            },
            buf.tobytes()
        )

    def send_dict(self, d):
        packed = msgpack.packb(serialise(d), use_bin_type=True)
        self._send({"type":"dict","length":len(packed)}, packed)

    def send_vertex_buffer(
        self,
        vertices: np.ndarray,
        tag: str,
        frame_index: int,
        visible: bool = True,
    ):
        """
        vertices: np.ndarray of shape (N,3), dtype float32, C-contiguous
        tag: either 'left' or 'right'
        """
        assert vertices.dtype == np.float32
        assert vertices.flags['C_CONTIGUOUS']
        byte_payload = vertices.tobytes()  # 4 bytes × (N*3)

        meta = {
            'type':        'mesh_buffer',
            'tag':         tag, 
            'numVerts':    int(vertices.shape[0]),
            'dtype':       'float32',
            'frameIndex':  frame_index,
            'visible':     bool(visible),
        }
        self._send(meta, byte_payload)
