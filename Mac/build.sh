#!/bin/sh
# 构建 Mac 版：通用二进制(arm64 + x86_64) → build/SrunAutoLogin.app
# 只需 Xcode Command Line Tools(xcode-select --install)，不需要 Xcode。
set -e
cd "$(dirname "$0")"

swift build -c release --triple arm64-apple-macosx14.0
swift build -c release --triple x86_64-apple-macosx14.0

APP=build/SrunAutoLogin.app
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS"
lipo -create \
    .build/arm64-apple-macosx/release/SrunAutoLogin \
    .build/x86_64-apple-macosx/release/SrunAutoLogin \
    -output "$APP/Contents/MacOS/SrunAutoLogin"
cp Info.plist "$APP/Contents/"
codesign --force --sign - "$APP"   # ad-hoc 签名；分发需公证请换成开发者证书

"$APP/Contents/MacOS/SrunAutoLogin" --selftest

# DMG：打开后把 app 拖到旁边的 Applications 即可安装
DMG=build/SrunAutoLogin.dmg
STAGE=build/dmg
rm -rf "$STAGE" "$DMG"
mkdir -p "$STAGE"
cp -R "$APP" "$STAGE/"
ln -s /Applications "$STAGE/Applications"
hdiutil create -volname "校园网自动登录" -srcfolder "$STAGE" -format UDZO -ov -quiet "$DMG"
rm -rf "$STAGE"

echo "OK: $APP"
echo "OK: $DMG"
