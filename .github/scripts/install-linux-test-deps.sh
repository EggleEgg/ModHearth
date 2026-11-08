#!/usr/bin/env bash
# Installs the native dependencies required to build ModHearth and run its Avalonia/Skia
# test suite on non-Debian Linux distributions. Intended for CI containers running as root.
#
# Usage: install-linux-test-deps.sh <fedora|arch|opensuse>

set -euo pipefail

distro="${1:?usage: install-linux-test-deps.sh <fedora|arch|opensuse>}"

# Shared rationale for each group below:
#   git/curl/tar/gzip    - checkout and dotnet-install.sh prerequisites
#   libicu/libstdc++/zlib/openssl/krb5 - .NET 8 runtime native dependencies
#   fontconfig/freetype/dejavu - text shaping/fallback used by Skia
#   xvfb/xauth + libX*   - Avalonia's X11 backend needs a display during UI smoke tests
#   mesa                 - software OpenGL fallback for Skia's GL surface
case "$distro" in
    fedora)
        dnf install -y --setopt=install_weak_deps=False \
            git curl tar gzip findutils which ca-certificates \
            libicu libstdc++ zlib-ng-compat openssl-libs krb5-libs \
            fontconfig freetype dejavu-sans-fonts \
            xorg-x11-server-Xvfb xorg-x11-xauth \
            libX11 libXext libXrender libXi libXrandr libXcursor libXfixes \
            libXcomposite libXdamage libxcb libxkbcommon libICE libSM mesa-libGL
        ;;
    arch)
        pacman -Syu --noconfirm --needed \
            git curl tar gzip findutils which ca-certificates \
            icu libstdc++ zlib openssl krb5 \
            fontconfig freetype2 ttf-dejavu \
            xorg-server-xvfb xorg-xauth \
            libx11 libxext libxrender libxi libxrandr libxcursor libxfixes \
            libxcomposite libxdamage libxcb libxkbcommon libice libsm mesa
        ;;
    opensuse)
        zypper --non-interactive --gpg-auto-import-keys refresh
        zypper --non-interactive install --no-recommends \
            git curl tar gzip findutils which ca-certificates \
            icu libstdc++6 libgcc_s1 libz1 libopenssl3 krb5 \
            fontconfig libfreetype6 dejavu-fonts \
            xorg-x11-server-Xvfb xvfb-run xauth \
            libX11-6 libXext6 libXrender1 libXi6 libXrandr2 libXcursor1 libXfixes3 \
            libXcomposite1 libXdamage1 libxcb1 libxkbcommon0 libICE6 libSM6 Mesa-libGL1
        ;;
    *)
        echo "Unsupported distribution: $distro" >&2
        exit 2
        ;;
esac
