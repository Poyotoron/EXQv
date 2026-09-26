# 手持ちの QvPen で使う

`BodyQv` / `GrabQv` / `BodyGrabQv` の Prefab は、QvPen 一式ごと置くためのものです。ワールドにすでに置いてある QvPen（色や見た目を着せ替えたものなど）を BodyQv や GrabQv のペンにしたいときは、**管理だけの Prefab** を置いて、そのペンを対象に指定します。

## BodyQv の場合

1. `Packages/EXQv/Prefabs/BodyQv/BodyQvManager` をシーンに置きます（置く位置はどこでも構いません）。
2. `BodyQvManager` を選び、Inspector の **「シーン内の QvPen を一覧から追加」** を押します。
3. 開いた一覧で、体に描けるようにしたいペン（`PenManager`）にチェックを入れ、**「選択したペンを追加」** を押します。
4. [レイヤーの設定](layers.md)の警告が出ていれば、ボタンで直します。

## GrabQv の場合

1. `Packages/EXQv/Prefabs/GrabQv/GrabQvManager` をシーンに置きます。
2. `GrabQvManager` を選び、Inspector の **「シーンの QvPen から対象を追加」** で、持てるものを描くペンを追加します。
3. **「区切りボタンを作る」** を押して、追加したペンに Split ボタンを付けます。

対象から外したペンの Split ボタンが残っていると、Inspector に警告と **「不要な区切りボタンを消す」** ボタンが出ます。

## 注意

- 対象に指定するのは、QvPen の **`PenManager`**（`Pens/PenManager (n)`）です。ペン本体（`Pen`）ではありません。一覧から選べば正しく入ります。
- **同じペンを BodyQv と GrabQv の両方に指定すると、そのペンは組み合わせで動きます**（[BodyQv + GrabQv](bodygrabqv.md)）。上の 2 つの手順を両方行い、両方の Inspector に「組み合わせて動くペン」の案内が出れば設定できています。組み合わせ用の管理はなく、2 つの管理のつながりはエディタが自動で設定します。
- 同じペンを対象にする `BodyQvManager` が 2 つ以上あると、どちらと組み合わせるか決められないため、GrabQv の Inspector に警告が出て組み合わせになりません。1 つにしてください。
- 対象に指定していない QvPen は、今までどおりふつうのペンとして動きます。
- 1 つの管理に、複数のペンをまとめて指定できます。ペンを増やすときは、今ある管理に足してください。
