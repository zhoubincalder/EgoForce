#!/usr/bin/env python3

import os
import sys  

ROOT_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.append(ROOT_DIR)

import argparse
import cv2
import numpy as np
import aria.sdk as aria
import threading

from camera_models import OVR624CameraModel
from settings import config as cfg
from unity_utils import HandVisibilityTracker, SendToUnity


DEFAULT_CAMERA_WIDTH = 1408
DEFAULT_CAMERA_HEIGHT = 1408

DEFAULT_F = np.array([1220.38417234667, 1220.38417234667], dtype=np.float32) / 2
DEFAULT_C = np.array([1459.308327420149, 1446.481271789112], dtype=np.float32) / 2

DEFAULT_PARAMS = np.array([
0.3881739923440562,
-0.3505272968594388,
-0.2039745469127034,
1.616037232456187,
-1.99366280389576,
0.7186532253554115,
0.0004348725659534717,
0.0001491800990352849,
0.0007271366281008595,
2.078482331496759e-06,
-0.0001256546435329295,
-0.0001402891608396858
], dtype=np.float32)


def build_camera_model(width=DEFAULT_CAMERA_WIDTH, height=DEFAULT_CAMERA_HEIGHT):
    scale = np.array(
        [width / DEFAULT_CAMERA_WIDTH, height / DEFAULT_CAMERA_HEIGHT],
        dtype=np.float32,
    )
    f = DEFAULT_F * scale
    c = DEFAULT_C * scale
    return OVR624CameraModel(f, c, params=DEFAULT_PARAMS.copy(), width=width, height=height)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Run the Aria demo over USB by default, or over Wi-Fi when an interface/IP is provided."
    )
    parser.add_argument(
        "--ip",
        default=os.environ.get("ARIA_DEVICE_IP"),
        help="IPv4 address of the Aria device. Required for Wi-Fi interfaces.",
    )
    parser.add_argument(
        "--interface",
        default=os.environ.get("ARIA_STREAMING_INTERFACE", "Usb"),
        choices=["Usb", "WifiStation"],
        help="Aria streaming transport to use. For Aria Gen 1 Python SDK, the supported options are Usb and WifiStation.",
    )
    parser.add_argument(
        "--profile",
        default=os.environ.get("ARIA_STREAMING_PROFILE", "profile21"),
        help="Streaming profile name.",
    )
    parser.add_argument(
        "--window",
        default="Aria Inference Demo",
        help="OpenCV window title.",
    )
    parser.add_argument(
        "--wait-ms",
        type=int,
        default=1,
        help="OpenCV waitKey delay in milliseconds.",
    )
    return parser.parse_args()


def env_flag(name, default=True):
    """Read a boolean environment flag without making the run script argparse-heavy."""
    value = os.environ.get(name)
    if value is None:
        return default
    return value.strip().lower() not in {"0", "false", "no", "off", ""}


def make_limb_vertex_buffer(outs, hand_index):
    """Return one contiguous float32 hand+arm vertex buffer for Unity."""
    limb_vertices = np.concatenate(
        [outs["pred_vertices"][hand_index], outs["pred_arm_vertices"][hand_index]],
        axis=0,
    )
    return np.ascontiguousarray(limb_vertices, dtype=np.float32)


def send_outputs_to_unity(
    unity_socket,
    image_bgr,
    outs,
    frame_index,
    visibility_tracker=None,
    send_image=True,
):
    """
    Send the current Aria frame to Unity.

    Unity receives two mesh buffers:
        - leftLimb:  hand vertices followed by arm vertices
        - rightLimb: hand vertices followed by arm vertices

    Each mesh buffer also carries a visibility flag so Unity can hide a hand after
    a configurable number of consecutive missed detections, while reusing the last
    valid pose during the grace period before hiding.

    The image path intentionally uses BGR because SendToUnity.send_image(...)
    expects BGR and internally converts BGR -> RGB before JPEG encoding.
    """
    left_limb = make_limb_vertex_buffer(outs, 0)
    right_limb = make_limb_vertex_buffer(outs, 1)
    hand_visible = outs.get("visible_hand", np.ones(2, dtype=bool))

    if visibility_tracker is not None:
        limb_meshes, unity_visible = visibility_tracker.resolve_mesh_buffers(
            hand_visible,
            [left_limb, right_limb],
        )
        left_limb, right_limb = limb_meshes
    else:
        unity_visible = np.asarray(hand_visible, dtype=bool).reshape(-1)

    unity_socket.send_vertex_buffer(
        left_limb,
        "leftLimb",
        frame_index,
        visible=unity_visible[0],
    )
    unity_socket.send_vertex_buffer(
        right_limb,
        "rightLimb",
        frame_index,
        visible=unity_visible[1],
    )

    if send_image:
        unity_socket.send_image(image_bgr, frame_index)

        
class StreamingClientObserver:
    def __init__(self):
        self._lock = threading.Lock()
        self.rgb_data = None
        self.frame_seq = 0

    def on_image_received(self, image: np.ndarray, image_record) -> None:
        if image_record.camera_id == aria.CameraId.Rgb:
            with self._lock:
                self.frame_seq += 1
                self.rgb_data = {
                    "rgb_image": image,
                    "capture_timestamp_ns": image_record.capture_timestamp_ns,
                    "frame_seq": self.frame_seq,
                }

    def get_latest_data(self, clear=True):
        with self._lock:
            if self.rgb_data is None:
                return None

            data = self.rgb_data
            if clear:
                self.rgb_data = None
            return data


def connect_to_device(args: argparse.Namespace):
    if args.interface != "Usb" and not args.ip:
        raise RuntimeError(
            "No Aria device IP was provided. Use --ip or set ARIA_DEVICE_IP when using Wi-Fi."
        )

    device_client = aria.DeviceClient()
    client_config = aria.DeviceClientConfig()
    if args.interface != "Usb" and args.ip:
        client_config.ip_v4_address = args.ip
    device_client.set_client_config(client_config)

    device = device_client.connect()
    streaming_manager = device.streaming_manager
    streaming_client = streaming_manager.streaming_client

    streaming_config = aria.StreamingConfig()
    streaming_config.profile_name = args.profile
    streaming_config.streaming_interface = getattr(aria.StreamingInterface, args.interface)
    streaming_config.security_options.use_ephemeral_certs = True
    streaming_manager.streaming_config = streaming_config
    streaming_manager.start_streaming()

    subscription_config = streaming_client.subscription_config
    subscription_config.subscriber_data_type = aria.StreamingDataType.Rgb
    subscription_config.message_queue_size[aria.StreamingDataType.Rgb] = 1

    options = aria.StreamingSecurityOptions()
    options.use_ephemeral_certs = True
    subscription_config.security_options = options
    streaming_client.subscription_config = subscription_config

    return device_client, device, streaming_manager, streaming_client


def aria_device_inference(args: argparse.Namespace):
    aria.set_log_level(aria.Level.Trace)

    device_client, device, streaming_manager, streaming_client = connect_to_device(args)

    streaming_state = streaming_manager.streaming_state
    print(f"Streaming state: {streaming_state}")

    from inference import Inference
    from demo_utils import brighten_rgb

    inference = Inference(build_camera_model())

    enable_unity = env_flag("UNITY_ENABLE", default=True)
    unity_send_image = env_flag("UNITY_SEND_IMAGE", default=True)
    unity_socket = SendToUnity() if enable_unity else None
    unity_visibility_tracker = None
    if unity_socket is not None:
        unity_visibility_tracker = HandVisibilityTracker(
            cfg.UNITY.HAND_VISIBILITY_MISS_THRESHOLD
        )
        print("Unity streaming enabled: tcp://*:5555")

    observer = StreamingClientObserver()
    streaming_client.set_streaming_client_observer(observer)
    streaming_client.subscribe()

    try:
        cv2.namedWindow(args.window, cv2.WINDOW_NORMAL)

        if args.interface == "Usb":
            print("Connected to Aria device over Usb")
        else:
            print(f"Connected to Aria device at {args.ip} over {args.interface}")
        print("Press q or Esc to quit")

        latest_display_bgr = None

        while True:
            data = observer.get_latest_data(clear=True)
            if data is not None:
                frame_seq = data["frame_seq"]
                rgb_image = np.rot90(data["rgb_image"], -1)
                rgb_image = brighten_rgb(rgb_image)
                display_bgr = cv2.cvtColor(rgb_image, cv2.COLOR_RGB2BGR)

                outs = inference.run_outputs(
                    rgb_image.copy(),
                    inference.device,
                )
                latest_display_bgr = display_bgr

                if unity_socket is not None:
                    send_outputs_to_unity(
                        unity_socket,
                        display_bgr,
                        outs,
                        frame_seq,
                        visibility_tracker=unity_visibility_tracker,
                        send_image=unity_send_image,
                    )

            if latest_display_bgr is not None:
                cv2.imshow(args.window, latest_display_bgr)

            key = cv2.waitKey(args.wait_ms) & 0xFF
            if key in (27, ord("q")):
                break
            if cv2.getWindowProperty(args.window, cv2.WND_PROP_VISIBLE) < 1:
                break
    except KeyboardInterrupt:
        print("Exiting...") 
    finally:
        streaming_client.unsubscribe()
        streaming_manager.stop_streaming()
        device_client.disconnect(device)
        if hasattr(cv2, "destroyAllWindows"):
            cv2.destroyAllWindows()


def main():
    args = parse_args()
    aria_device_inference(args)



if __name__ == "__main__":
    main()
