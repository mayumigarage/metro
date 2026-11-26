---
name: release
description: metro の Windows 版をビルドして GitHub リリースに出す。「ビルドしてリリース」「リリースして」「新しい版を出して」と言われたときに使う。
---

# ビルドしてリリース

Windows 64bit 版を CLI でビルドし、zip を GitHub リリース (`gh`) に添付する。
前提: Unity エディタがこのプロジェクトを開いている (`unity status`。なければ `unity open .`)、`gh auth status` が通る。

## 手順

1. **状態確認**: `git status -sb`、`git fetch`、`gh release list`。
   - 未コミットの変更があれば、コミットして含めるかをユーザーに聞く。勝手にコミットや退避をしない。
   - リリースは `main` から出す。別のブランチにいる、または origin より遅れている場合は止めて伝える。
2. **バージョン**: 指定がなければ最新リリースのパッチ番号を 1 上げた値を提案し、確認を取る。
3. **bundleVersion を合わせる** (違う場合だけ)。エディタが開いているのでファイルを直接編集しない。
   ```powershell
   unity command --project-path . "script eval" --code 'UnityEditor.PlayerSettings.bundleVersion = "X.Y.Z"; UnityEditor.AssetDatabase.SaveAssets(); return UnityEditor.PlayerSettings.bundleVersion;'
   ```
   差分が `bundleVersion` だけであることを確認してコミットする。
4. **push**: `git push origin main` (タグの対象コミットが GitHub 上に必要)。
5. **ビルド**:
   ```powershell
   pwsh .claude/skills/release/build.ps1 -Version X.Y.Z
   ```
   コンパイル確認、ビルド、8 秒の起動確認、`ProjectSettings.asset` の自動変更の取り消し、`DoNotShip` を除いた zip 化まで行う。出力は `Builds/vX.Y.Z/`。失敗したらリリースせずに原因を報告する。
6. **公開**: リリースノートは前のタグからの変更 (`git log vA.B.C..HEAD --oneline`) を日本語で短くまとめ、遊び方 (zip を展開して `metro/metro.exe` を実行) を添える。
   ```powershell
   gh release create vX.Y.Z Builds/vX.Y.Z/metro-vX.Y.Z-win64.zip --target <build.ps1 が出した commit> --title "metro vX.Y.Z" --notes-file <ノートのファイル>
   ```
7. **確認**: `gh release view vX.Y.Z --json url,assets` でアップロードを確かめ、URL を伝える。起動確認は 8 秒だけで、プレイ操作は未確認だと伝える。

## 注意

- 同じバージョンのリリースやタグがすでにある場合は上書きしない。ユーザーに確認する。
- リポジトリは非公開。公開設定を変える依頼があった場合は、収録曲の再配布の可否を先に確認する。
