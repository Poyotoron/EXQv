# インストール

## 動作環境

| 項目 | 要件 |
|---|---|
| Unity | 2022.3 (LTS) |
| VRChat SDK | Worlds 3.8.1 以上 |
| QvPen | 3.3.15 以上 3.4.0 未満（動作確認は 3.3.15） |

!!! note "QvPen は別に導入します"
    EXQv は QvPen を同梱していません。先に QvPen（`net.ureishi.qvpen`）をプロジェクトに入れてください。VCC / ALCOM から入れる場合は、EXQv を追加するときに自動で入ります。

## VCC / ALCOM から導入する（推奨）

1. VCC または ALCOM に、配布元のリポジトリを追加します。
2. 対象のワールドの Unity プロジェクトを開き、**Manage Packages** を選びます。
3. 一覧から「EXQv」を追加します。

## unitypackage から導入する

VCC を使っていない場合は、[リリースページ](https://github.com/Poyotoron/EXQv/releases)から `.unitypackage` をダウンロードし、Unity のプロジェクトへドラッグ＆ドロップしてインポートしてください。QvPen は別途導入が必要です。

## 導入できたか確認する

Unity の Project ウィンドウで、`Packages` の下に **EXQv** が増えていればインストール成功です。中に次の Prefab があります。

| Prefab | 場所 | 中身 |
|---|---|---|
| `BodyQv` | `Packages/EXQv/Prefabs/BodyQv/` | 体に描ける QvPen 一式（ペン・消しゴム・管理） |
| `BodyQvManager` | 同上 | 管理だけ。[手持ちの QvPen](existing-pens.md) を対象にするとき |
| `GrabQv` | `Packages/EXQv/Prefabs/GrabQv/` | 描いたものを持てる QvPen 一式（ペン・消しゴム・区切りボタン・持ち手・管理） |
| `GrabQvManager` | 同上 | 管理と持ち手だけ |

## アンインストール

VCC の Manage Packages から削除します。シーンに置いた EXQv の Prefab は参照切れになるので、先にシーンから消しておいてください。

!!! warning "レイヤーの設定は元に戻りません"
    BodyQv の[レイヤーの設定](layers.md)で直した衝突の設定（プロジェクト設定）は、アンインストールしても残ります。気になる場合は、Project Settings の Physics で手で戻してください。
