#!/usr/bin/env bash
# Godot .NET 云端编辑器一键启动脚本（幂等，可重复运行）
set -e

GODOT_VERSION="4.6"
export DISPLAY=:1
export LIBGL_ALWAYS_SOFTWARE=1
export PATH="$PATH:/usr/share/dotnet:$HOME/.dotnet"
export DOTNET_ROOT="${DOTNET_ROOT:-/usr/share/dotnet}"

# ---------- [1/4] 系统依赖（只装一次） ----------
if [ ! -f "$HOME/.deps-done" ]; then
  echo "==> 安装系统依赖（约1-2分钟，只此一次）"
  sudo apt-get update -y
  sudo apt-get install -y --no-install-recommends \
    xvfb x11vnc fluxbox novnc websockify \
    libgl1 libegl1 libgl1-mesa-dri \
    libfontconfig1 libxkbcommon0 libxrandr2 libxinerama1 libxcursor1 libxi6 \
    libasound2t64 libpulse0 \
    wget unzip ca-certificates
  touch "$HOME/.deps-done"
else
  echo "==> 系统依赖已就绪，跳过"
fi

# ---------- [2/4] Godot .NET 编辑器（只下一次） ----------
if [ ! -x "$HOME/godot-editor/Godot.x86_64" ]; then
  echo "==> 下载 Godot ${GODOT_VERSION} .NET 版"
  mkdir -p "$HOME/godot-editor" && cd "$HOME/godot-editor"
  rm -rf Godot_v*-stable_mono_linux_x86_64 godot.zip
  wget -q "https://github.com/godotengine/godot/releases/download/${GODOT_VERSION}-stable/Godot_v${GODOT_VERSION}-stable_mono_linux_x86_64.zip" -O godot.zip
  unzip -o -q godot.zip && rm godot.zip
  BIN="$(find . -maxdepth 2 -type f -name 'Godot_v*_mono_linux.x86_64' | head -1)"
  echo "==> 找到二进制: $BIN"
  chmod +x "$BIN"
  ln -sf "$(realpath "$BIN")" "$HOME/godot-editor/Godot.x86_64"
else
  echo "==> Godot 已就绪，跳过下载"
fi

# ---------- [3/4] 虚拟桌面 + 浏览器画面串流 ----------
echo "==> 启动虚拟桌面与 noVNC"
pgrep -x Xvfb    >/dev/null || (Xvfb :1 -screen 0 1600x900x24 &)
sleep 1
pgrep -x fluxbox >/dev/null || (DISPLAY=:1 fluxbox &)
pgrep -x x11vnc  >/dev/null || (x11vnc -display :1 -nopw -forever -shared -rfbport 5900 -quiet &)
pgrep -f "websockify.*6080" >/dev/null || (websockify -D --web=/usr/share/novnc 6080 localhost:5900)

# ---------- [4/4] 启动 Godot 编辑器（自动打开当前仓库的工程） ----------
WORKSPACE_DIR="$(pwd)"  # postStartCommand 的运行目录 = 仓库根目录
if ! pgrep -f "Godot.x86_64" >/dev/null; then
  echo "==> 启动 Godot 编辑器"
  cd "$HOME/godot-editor"
  if [ -f "$WORKSPACE_DIR/project.godot" ]; then
    echo "==> 检测到本仓库的 project.godot，直接打开格战工程"
    nohup ./Godot.x86_64 --rendering-driver opengl3 --path "$WORKSPACE_DIR" > "$HOME/godot.log" 2>&1 &
  else
    nohup ./Godot.x86_64 --rendering-driver opengl3 > "$HOME/godot.log" 2>&1 &
  fi
fi

echo ""
echo "========================================"
echo " 全部就绪！"
echo " 打开 Codespaces 底部 Ports 面板里 6080 端口的地址，"
echo " 路径加上 /vnc.html ，点 Connect 即可看到 Godot。"
echo "========================================"
