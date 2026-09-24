# ドキュメントのローカル確認

共用の仮想環境を有効にします。Unity プロジェクト内には仮想環境を作らないでください。

```powershell
& "$env:USERPROFILE\.venvs\vpm-docs\Scripts\Activate.ps1"
```

実行ポリシーで拒否された場合は、現在の PowerShell プロセスだけ制限を緩和してから再度有効にします。

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
```

ローカルプレビューを起動します。

```powershell
mkdocs serve -f "docs~/mkdocs.yml"
```

公開前は、パッケージのバージョンを指定して厳格モードでビルドします。

```powershell
$env:BODYQV_VERSION = "0.1.0"; mkdocs build --strict -f "docs~/mkdocs.yml"
```

仮想環境がまだ無い場合だけ、次のコマンドで作成します。

```powershell
uv venv --python 3.13 "$env:USERPROFILE\.venvs\vpm-docs"
uv pip install -r "docs~/requirements.txt"
```

ビルド成果物 `docs~/site/` はコミットしません。`docs~/` の末尾の `~` は Unity が `.meta` を生成しないために必要なので、削除しないでください。
