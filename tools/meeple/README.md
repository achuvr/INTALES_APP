# ミープルの画像と3Dの駒

2026-11-10 に作成。形は一般的なミープルを一から描いたもの（公式の絵は使っていない）。

- `meeple.py` … 赤・背景透過の PNG（1024×1024・#D62828）。形の数値（`RIGHT` の輪郭の点・頭の位置と半径・角の丸み）はここにある
- `meeple_outline.py` … `meeple.py` と同じ形から、立体用の輪郭線（高さ1・足もとが0）を JSON で取り出す
- `make_meeple_3d.py` … Blender で輪郭線を厚みのある板にして前後のふちを丸め、Unity 向けの FBX にする
  （厚み `THICK`＝0.62・ふちの丸み `BEVEL`＝0.03。Y が上・1 単位＝Unity の 1・足もとの中心が原点）

- `meeple_icon.py` … 所持品のコマのアイコン（赤・青・黄の 512×512・背景透過。ふちに光と影）を書き出す（2026-11-10）。
  `python meeple_icon.py tools/meeple Assets/Resources/ItemIcon` で `MeepleRed/Blue/Yellow.png` を上書きする（.meta はそのまま）

Unity の駒は `Assets/Models/Meeple/Meeple.fbx`（マテリアル MeepleRed は取り込み時に URP の Lit で作られる）。

## 作り直す

形を変えるときは `meeple.py` の数値を直してから、順に実行する（`<作業フォルダ>` は好きな場所）。

```
python meeple.py <作業フォルダ>/meeple_red.png <作業フォルダ>/preview.png
python meeple_outline.py tools/meeple <作業フォルダ>/meeple_outline.json 256
"C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" --background --python tools/meeple/make_meeple_3d.py -- <作業フォルダ>/meeple_outline.json Assets/Models/Meeple/Meeple.fbx <作業フォルダ> <作業フォルダ>/Meeple.blend
```

`meeple_outline.py` の1つ目の引数は `meeple.py` のあるフォルダ。Blender の最後の2つは確認用の絵（3方向）と .blend の保存先。
