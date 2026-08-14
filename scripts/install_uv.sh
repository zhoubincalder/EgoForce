#!/usr/bin/env bash
#
# uv-based install for EgoForce.
#
# Creates ./.venv from pyproject.toml / uv.lock, then builds the CUDA extensions
# that have to compile against the already-installed torch.
#
# Notes on the pinned stack:
#   * torch 2.13.0 on CUDA 13.2 is the newest published set (torchvision 0.28.0,
#     torch_tensorrt 2.13.0) that still ships cp310 wheels.
#   * CUDA 13 covers Blackwell (RTX PRO 6000, RTX 50xx, B200). Those are sm_120,
#     and older cu126 wheels contain no sm_120 kernels, so they fail at the first
#     kernel launch on such cards.
#   * mmcv 2.2.0 is the last of the 2.x line and compiles against torch 2.13 /
#     CUDA 13. mmdet stays at 3.3.0 -- that is still the newest mmdet release.
#   * The CUDA toolkit must come from the system (/usr/local/cuda-*), since uv
#     cannot install nvcc.
#   * ffmpeg and git-lfs are system prerequisites -- see PREREQUISITES below.
#
# PREREQUISITES (Debian/Ubuntu):
#   sudo apt-get install -y build-essential git git-lfs ffmpeg
#   plus a CUDA 13.x toolkit under /usr/local/cuda-13.x (or set CUDA_HOME).
#
# Usage:
#   bash scripts/install_uv.sh
#   bash scripts/install_uv.sh --extra demo     # also install the gradio demo deps

set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" >/dev/null 2>&1 && pwd)"
REPO_ROOT="$(cd -- "${SCRIPT_DIR}/.." >/dev/null 2>&1 && pwd)"
cd "${REPO_ROOT}"

EXTRAS=()
while [[ $# -gt 0 ]]; do
    case "$1" in
        --extra) EXTRAS+=("--extra" "$2"); shift 2 ;;
        *) echo "unknown argument: $1" >&2; exit 1 ;;
    esac
done

command -v uv >/dev/null 2>&1 || {
    echo "uv is required but was not found on PATH." >&2
    echo "Install it with: curl -LsSf https://astral.sh/uv/install.sh | sh" >&2
    exit 1
}

# --- CUDA toolkit -----------------------------------------------------------
# torch is built against CUDA 13.2, so nvcc must be 13.x -- the extensions below
# link against the same CUDA runtime as torch, and a major-version mismatch is a
# hard error in torch.utils.cpp_extension.
if [[ -z "${CUDA_HOME:-}" ]]; then
    for candidate in /usr/local/cuda-13.3 /usr/local/cuda-13.2 /usr/local/cuda-13 /usr/local/cuda; do
        if [[ -x "${candidate}/bin/nvcc" ]] \
           && "${candidate}/bin/nvcc" --version | grep -q "release 13\."; then
            CUDA_HOME="${candidate}"; break
        fi
    done
fi
if [[ -z "${CUDA_HOME:-}" || ! -x "${CUDA_HOME}/bin/nvcc" ]]; then
    echo "No CUDA 13.x toolkit found. Set CUDA_HOME to one containing bin/nvcc." >&2
    exit 1
fi
export CUDA_HOME
export PATH="${CUDA_HOME}/bin:${PATH}"
export LD_LIBRARY_PATH="${CUDA_HOME}/lib64:${LD_LIBRARY_PATH:-}"

NVCC_MAJOR="$(nvcc --version | sed -n 's/.*release \([0-9]*\)\..*/\1/p')"
if [[ "${NVCC_MAJOR}" != "13" ]]; then
    echo "nvcc is CUDA ${NVCC_MAJOR}.x; torch 2.13.0+cu132 needs a 13.x nvcc." >&2
    exit 1
fi

# Build only for the GPUs actually present -- pytorch3d in particular takes far
# longer per extra architecture. Falls back to Ampere..Blackwell if there is no
# visible GPU (e.g. building on a CPU-only login node).
if [[ -z "${TORCH_CUDA_ARCH_LIST:-}" ]]; then
    detected="$(nvidia-smi --query-gpu=compute_cap --format=csv,noheader 2>/dev/null \
                | sort -u | paste -sd';' -)"
    TORCH_CUDA_ARCH_LIST="${detected:-8.0;8.6;9.0;12.0}"
fi
export TORCH_CUDA_ARCH_LIST
export FORCE_CUDA=1
export MMCV_WITH_OPS=1
export MAX_JOBS="${MAX_JOBS:-$(( $(nproc) > 24 ? 24 : $(nproc) ))}"

echo "==> CUDA_HOME=${CUDA_HOME}  archs=${TORCH_CUDA_ARCH_LIST}  MAX_JOBS=${MAX_JOBS}"

# --- 1. base environment ----------------------------------------------------
# --inexact matters on re-runs: the extensions built in step 2/3 are installed
# with `uv pip install` and are therefore not in uv.lock, so a plain `uv sync`
# considers them extraneous and uninstalls them. `uv run` does not prune, so
# day-to-day use is unaffected -- but always add --inexact when syncing by hand.
echo "==> uv sync (torch stack + pure-python deps)"
uv sync --inexact "${EXTRAS[@]+"${EXTRAS[@]}"}"

# Build backends for the --no-build-isolation installs below. setuptools is held
# at 81.0.0 to match scripts/install.sh; chumpy's setup.py breaks on newer ones.
uv pip install "setuptools==81.0.0" wheel ninja packaging

# --- 2. CUDA / C++ extensions ----------------------------------------------
# These compile against the installed torch, so they must not be built in an
# isolated PEP 517 environment.
#
# uv caches the wheels it builds, and that cache key does NOT include the torch
# version that was linked against. So after a torch upgrade uv will happily
# reinstall the *previously built* wheel, which then dies at import with
# `undefined symbol: _ZN3c10...`. Stamp the torch build the extensions were
# compiled against and force a real rebuild whenever it changes.
STAMP_FILE="${REPO_ROOT}/.venv/.egoforce-ext-stamp"
TORCH_STAMP="$(uv run --no-sync python -c 'import torch; print(torch.__version__)')"
REBUILD_FLAGS=()
if [[ ! -f "${STAMP_FILE}" || "$(cat "${STAMP_FILE}")" != "${TORCH_STAMP}" ]]; then
    if [[ -f "${STAMP_FILE}" ]]; then
        echo "==> torch changed ($(cat "${STAMP_FILE}") -> ${TORCH_STAMP}); forcing extension rebuild"
    fi
    REBUILD_FLAGS=(--no-cache)
fi

# mmcv 2.2.0 is the last release of the 2.x line. Using it requires the
# mmcv_maximum_version ceiling in thirdparty/mmdetection/mmdet/__init__.py to be
# raised from '2.2.0' to '2.3.0' -- that patch is applied to the vendored copy.
echo "==> mmcv"
uv pip install "mmcv==2.2.0" --no-build-isolation \
    "${REBUILD_FLAGS[@]+"${REBUILD_FLAGS[@]}"}" --reinstall-package mmcv

echo "==> anycalib / chumpy / pytorch3d (pytorch3d takes ~10-20 min)"
uv pip install "anycalib @ git+https://github.com/javrtg/AnyCalib.git" --no-build-isolation
uv pip install "chumpy @ git+https://github.com/mattloper/chumpy.git" --no-build-isolation
uv pip install "pytorch3d @ git+https://github.com/facebookresearch/pytorch3d.git" --no-build-isolation \
    "${REBUILD_FLAGS[@]+"${REBUILD_FLAGS[@]}"}" --reinstall-package pytorch3d

# --- 3. vendored thirdparty packages ---------------------------------------
echo "==> thirdparty/datapipes"
uv pip install "${REPO_ROOT}/thirdparty/datapipes"
rm -rf "${REPO_ROOT}/thirdparty/datapipes/build" "${REPO_ROOT}/thirdparty/datapipes"/*.egg-info

echo "==> thirdparty/mmdetection"
uv pip install "${REPO_ROOT}/thirdparty/mmdetection" --no-build-isolation
rm -rf "${REPO_ROOT}/thirdparty/mmdetection/build" "${REPO_ROOT}/thirdparty/mmdetection"/*.egg-info

# numpy must land back on 1.x: the extensions above are compiled against the
# numpy 1.x ABI. uv's override-dependencies pins it, this just re-asserts it
# after the non-locked installs above.
uv pip install "numpy==1.26.4"

# record which torch the extensions above were compiled against
echo "${TORCH_STAMP}" > "${STAMP_FILE}"

# --- 4. optional conveniences ----------------------------------------------
# demo/run_app.py probes for an `ffmpeg` next to the interpreter before falling
# back to PATH. Mirror the system binary into .venv/bin so that probe succeeds.
if [[ ! -e "${REPO_ROOT}/.venv/bin/ffmpeg" ]] && command -v ffmpeg >/dev/null 2>&1; then
    ln -s "$(command -v ffmpeg)" "${REPO_ROOT}/.venv/bin/ffmpeg"
fi

echo
echo "==> done. activate with:  source .venv/bin/activate"
echo "    or run commands with: uv run python experiments/save_predictions.py ..."
uv run python -c "
import torch
print(f'torch {torch.__version__}  cuda {torch.version.cuda}  available={torch.cuda.is_available()}')
if torch.cuda.is_available():
    print(f'gpu   {torch.cuda.get_device_name(0)}  sm_{\"\".join(map(str, torch.cuda.get_device_capability(0)))}')
"
