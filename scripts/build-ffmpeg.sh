#!/bin/bash
# Builds the FFmpeg the app ships: LGPL 2.1, only the parts the app uses. scripts/build-ffmpeg.ps1
# runs this inside MSYS2's UCRT64 environment; its arguments are the work folder and the output folder.
#
# LAME is the one LGPL library FFmpeg links, so it stays a separate DLL that can be replaced.
# The others have BSD-style licenses and are linked into the FFmpeg DLLs.
set -euo pipefail

WORK=$(cygpath -u "$1")
OUT=$(cygpath -u "$2")
SCRIPTS=$(cd "$(dirname "$0")" && pwd)
SOURCES=$WORK/sources
BUILD=$WORK/build
PREFIX=$WORK/prefix
JOBS=$(nproc)

export PKG_CONFIG_PATH=$PREFIX/lib/pkgconfig
export CFLAGS="-O2 -I$PREFIX/include"
export LDFLAGS="-L$PREFIX/lib -static-libgcc"

# name  url  sha256
PINNED="
ffmpeg-n9.0.2.tar.gz https://github.com/FFmpeg/FFmpeg/archive/refs/tags/n9.0.2.tar.gz 6e374ed621e48faa40639307dff48ba6fe574a509977956d2cce9669b7cc27e9
lame-3.100.tar.gz https://downloads.sourceforge.net/project/lame/lame/3.100/lame-3.100.tar.gz ddfe36cab873794038ae2c1210557ad34857a4b6bdc515785d1da9e175b1da1e
libogg-1.3.6.tar.xz https://github.com/xiph/ogg/releases/download/v1.3.6/libogg-1.3.6.tar.xz 5c8253428e181840cd20d41f3ca16557a9cc04bad4a3d04cce84808677fa1061
libvorbis-1.3.7.tar.xz https://github.com/xiph/vorbis/releases/download/v1.3.7/libvorbis-1.3.7.tar.xz b33cc4934322bcbf6efcbacf49e3ca01aadbea4114ec9589d1b1e9d20f72954b
opus-1.5.2.tar.gz https://github.com/xiph/opus/releases/download/v1.5.2/opus-1.5.2.tar.gz 65c1d2f78b9f2fb20082c38cbe47c951ad5839345876e46941612ee87f9a7ce1
libvpx-1.17.0.tar.gz https://github.com/webmproject/libvpx/archive/refs/tags/v1.17.0.tar.gz 1020f184046187baa2985dbde38e0691f49c44088bca7a1842b0236c6081dc0a
dav1d-1.5.4.tar.xz https://downloads.videolan.org/pub/videolan/dav1d/1.5.4/dav1d-1.5.4.tar.xz 686616b7c69eb88d44459391ab25cac13b6647a3b288835c5784e71c1514a5c5
zlib-1.3.2.tar.xz https://github.com/madler/zlib/releases/download/v1.3.2/zlib-1.3.2.tar.xz d7a0654783a4da529d1bb793b7ad9c3318020af77667bcae35f95d0e42a792f3
"

mkdir -p "$SOURCES"
rm -rf "$BUILD" "$PREFIX"
mkdir -p "$BUILD" "$PREFIX/include" "$PREFIX/lib"

while read -r name url sum; do
    [ -n "$name" ] || continue
    [ -f "$SOURCES/$name" ] || curl -fsSL --retry 3 -o "$SOURCES/$name" "$url"
    echo "$sum  $SOURCES/$name" | sha256sum -c --quiet -
    mkdir -p "$BUILD/${name%%.tar.*}"
    tar -xf "$SOURCES/$name" -C "$BUILD/${name%%.tar.*}" --strip-components=1
done <<< "$PINNED"

echo "== zlib"
cd "$BUILD/zlib-1.3.2"
make -j"$JOBS" -f win32/Makefile.gcc libz.a
cp libz.a "$PREFIX/lib/"
cp zlib.h zconf.h "$PREFIX/include/"

echo "== LAME"
cd "$BUILD/lame-3.100"
# The symbol list names a function this configuration doesn't build, and linking stops on it.
sed -i '/lame_init_old/d' include/libmp3lame.sym
./configure --prefix="$PREFIX" --enable-shared --disable-static --disable-frontend --disable-gtktest --enable-nasm
make -j"$JOBS"
make install

echo "== Ogg"
cd "$BUILD/libogg-1.3.6"
./configure --prefix="$PREFIX" --disable-shared --enable-static
make -j"$JOBS"
make install

echo "== Vorbis"
cd "$BUILD/libvorbis-1.3.7"
./configure --prefix="$PREFIX" --disable-shared --enable-static --disable-docs --disable-examples --disable-oggtest --with-ogg="$PREFIX"
make -j"$JOBS"
make install

echo "== Opus"
cd "$BUILD/opus-1.5.2"
./configure --prefix="$PREFIX" --disable-shared --enable-static --disable-doc --disable-extra-programs --disable-stack-protector
make -j"$JOBS"
make install

echo "== libvpx (VP9 encoder only; FFmpeg decodes VP8 and VP9 itself)"
cd "$BUILD/libvpx-1.17.0"
./configure --prefix="$PREFIX" --target=x86_64-win64-gcc --as=nasm --disable-shared --enable-static \
    --disable-examples --disable-tools --disable-docs --disable-unit-tests \
    --disable-vp8 --enable-vp9 --disable-vp9-decoder
make -j"$JOBS"
make install
# libvpx's threads come from winpthreads, which would otherwise be one more DLL to ship.
sed -i 's/-lpthread/-l:libwinpthread.a/' "$PREFIX/lib/pkgconfig/vpx.pc"

echo "== dav1d (AV1 decoder)"
cd "$BUILD/dav1d-1.5.4"
CFLAGS= LDFLAGS= meson setup out --prefix="$PREFIX" --libdir=lib --buildtype=release --default-library=static \
    -Denable_tools=false -Denable_tests=false
ninja -C out install

echo "== FFmpeg"
cd "$BUILD/ffmpeg-n9.0.2"

# Everything the app can be handed: see Formats/catalog.json and SafeInputOptions in FfmpegArguments.cs.
DEMUXERS=mov,matroska,avi,asf,mp3,wav,flac,ogg,aiff,gif
MUXERS=mp4,mov,ipod,matroska,webm,avi,asf,mp3,wav,flac,ogg,opus,gif,image2,null
VIDEO_DECODERS=h264,hevc,vp8,vp9,libdav1d,mpeg4,msmpeg4v1,msmpeg4v2,msmpeg4v3,mpeg1video,mpeg2video,h263,wmv1,wmv2,wmv3,vc1,mjpeg,gif,png,bmp,theora,prores,dvvideo,rawvideo,qtrle,ffv1,huffyuv,utvideo,cinepak,indeo5
AUDIO_DECODERS=aac,aac_latm,mp3,mp3float,mp2,mp2float,flac,vorbis,opus,alac,ac3,eac3,dca,truehd,wmav1,wmav2,wmapro,wmalossless,amrnb,amrwb,adpcm_ima_wav,adpcm_ms,pcm_s8,pcm_u8,pcm_s16le,pcm_s16be,pcm_s24le,pcm_s24be,pcm_s32le,pcm_s32be,pcm_f32le,pcm_f32be,pcm_f64le,pcm_f64be,pcm_alaw,pcm_mulaw
ENCODERS=h264_mf,aac_mf,libvpx_vp9,libopus,libmp3lame,libvorbis,flac,pcm_s16le,gif,png
PARSERS=h264,hevc,vp8,vp9,av1,mpeg4video,mpegvideo,mjpeg,h263,vc1,png,gif,aac,aac_latm,mpegaudio,flac,vorbis,opus,ac3,dca
FILTERS=crop,pad,scale,fps,format,null,setsar,setpts,trim,concat,split,palettegen,paletteuse,hflip,vflip,transpose,rotate,aformat,anull,aresample,asetpts,atrim,showwavespic

# mfenc.c, FFmpeg's H.264 and AAC through Windows, doesn't compile without d3d11va.

# The configure line ends up in the binaries, where "ffmpeg -version" prints it. So no paths of this
# machine on it: the compiler finds our libraries through its environment instead.
export CPATH=$PREFIX/include LIBRARY_PATH=$PREFIX/lib
./configure --prefix=/ffmpeg \
    --pkg-config-flags=--static \
    --extra-ldflags=-static-libgcc \
    --extra-libs=-l:libwinpthread.a \
    --enable-shared --disable-static \
    --disable-debug --disable-doc --disable-ffplay --disable-avdevice \
    --disable-autodetect --disable-network --disable-everything \
    --enable-mediafoundation --enable-d3d11va --enable-zlib \
    --enable-libmp3lame --enable-libvorbis --enable-libopus --enable-libvpx --enable-libdav1d \
    --enable-protocol=file,pipe \
    --enable-demuxer=$DEMUXERS --enable-muxer=$MUXERS \
    --enable-decoder=$VIDEO_DECODERS,$AUDIO_DECODERS --enable-encoder=$ENCODERS \
    --enable-parser=$PARSERS --enable-filter=$FILTERS --enable-bsfs
make -j"$JOBS"
make install DESTDIR="$PREFIX"

echo "== Output"
rm -rf "$OUT"
mkdir -p "$OUT/licenses"
cp "$PREFIX"/ffmpeg/bin/{ffmpeg.exe,ffprobe.exe} "$PREFIX"/ffmpeg/bin/av*.dll "$PREFIX"/ffmpeg/bin/sw*.dll "$OUT/"
cp "$PREFIX"/bin/libmp3lame-0.dll "$OUT/"
strip "$OUT"/*.exe "$OUT"/*.dll

cp "$BUILD/ffmpeg-n9.0.2/COPYING.LGPLv2.1" "$OUT/licenses/FFmpeg-LGPLv2.1.txt"
cp "$BUILD/ffmpeg-n9.0.2/LICENSE.md" "$OUT/licenses/FFmpeg-LICENSE.md"
cp "$BUILD/lame-3.100/COPYING" "$OUT/licenses/LAME-LGPL.txt"
cp "$BUILD/libogg-1.3.6/COPYING" "$OUT/licenses/libogg.txt"
cp "$BUILD/libvorbis-1.3.7/COPYING" "$OUT/licenses/libvorbis.txt"
cp "$BUILD/opus-1.5.2/COPYING" "$OUT/licenses/opus.txt"
cp "$BUILD/libvpx-1.17.0/LICENSE" "$OUT/licenses/libvpx.txt"
cp "$BUILD/libvpx-1.17.0/PATENTS" "$OUT/licenses/libvpx-PATENTS.txt"
cp "$BUILD/dav1d-1.5.4/COPYING" "$OUT/licenses/dav1d.txt"
cp "$BUILD/zlib-1.3.2/LICENSE" "$OUT/licenses/zlib.txt"
cp /ucrt64/share/licenses/libwinpthread/COPYING "$OUT/licenses/winpthreads.txt"
cp /ucrt64/share/licenses/crt/COPYING.MinGW-w64-runtime.txt "$OUT/licenses/mingw-w64-runtime.txt"

echo "== Checks"
cd "$OUT"
BUILDCONF=$(./ffmpeg.exe -hide_banner -buildconf)
if echo "$BUILDCONF" | grep -qE -- '--enable-(gpl|nonfree|version3)'; then
    echo "This build is not plain LGPL 2.1. Do not ship it." >&2
    exit 1
fi
./ffmpeg.exe -hide_banner -L | grep -q 'Lesser General Public License' || { echo "Unexpected license." >&2; exit 1; }

# Nothing but our own DLLs and Windows' may be needed, or the build only works on this machine.
UNEXPECTED=$(for f in *.exe *.dll; do objdump -p "$f" | sed -n 's/^\s*DLL Name: //p'; done | sort -u \
    | grep -viE '^(avcodec|avfilter|avformat|avutil|swresample|swscale)-[0-9]+\.dll$|^libmp3lame-0\.dll$|^(kernel32|user32|advapi32|ole32|oleaut32|shell32|shlwapi|ws2_32|bcrypt|secur32|gdi32|mf|mfplat|mfuuid|mfreadwrite|psapi|ucrtbase|msvcrt|vcruntime140)\.dll$|^api-ms-win-[a-z0-9-]+\.dll$' || true)
if [ -n "$UNEXPECTED" ]; then
    echo "The build needs DLLs that won't be there: $UNEXPECTED" >&2
    exit 1
fi

{
    echo "Built by scripts/build-ffmpeg.ps1. Sources (name, address, SHA-256):"
    echo "$PINNED" | sed '/^$/d'
    echo
    echo "Toolchain:"
    pacman -Q mingw-w64-ucrt-x86_64-gcc mingw-w64-ucrt-x86_64-binutils mingw-w64-ucrt-x86_64-crt-git mingw-w64-ucrt-x86_64-nasm
    echo
    ./ffmpeg.exe -hide_banner -version
} > BUILD-INFO.txt

# What anyone needs to rebuild it: the LGPL's corresponding source.
BUNDLE=$WORK/source-bundle
rm -rf "$BUNDLE"
mkdir -p "$BUNDLE"
cp "$SOURCES"/*.tar.* "$BUNDLE/"
cp "$SCRIPTS/build-ffmpeg.ps1" "$SCRIPTS/build-ffmpeg.sh" BUILD-INFO.txt "$BUNDLE/"
cp "$OUT/licenses/FFmpeg-LGPLv2.1.txt" "$BUNDLE/"

du -sh "$OUT"
echo "FFmpeg is ready in $OUT"
