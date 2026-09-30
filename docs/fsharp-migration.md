# F# 移行と配布の検証記録

検証日: 2026-10-01

現行 PoC を F# に移植した。機能拡張と移植を分離し、EARS モデル生成・binding・検証器・skill は今回追加していない。

## 開発環境

- proto で .NET SDK 10.0.401 をインストールした。
- `.prototools` と `global.json` で SDK を固定。target framework は `net10.0`。
- 既存の Homebrew .NET 9 やグローバルの proto 設定は変更していない。開発コマンドは `proto run dotnet -- …` を使用する。
- Markdig 1.4.0、FSharp.SystemTextJson 1.4.36、SDK の FSharp.Core 10.1.401 を使用。各プロジェクトの `packages.lock.json` を保持する。
- `scripts/check.sh` は locked restore、警告をエラー扱いにした build、テストを順に実行する。NuGet の初回取得と脆弱性情報の取得にはネットワーク接続が必要。

## 構成

```text
Markdown 入力
  → Markdown.fs (Markdig AST adapter)
  → Parser.fs (cc-sdd profile)
  → Domain.fs の DesignIR
      → Rules.fs
      → SemanticDiff.fs ← baseline の DesignIR
  → Report.fs (最大3件の Human Review Focus / Markdown)
  → Program.fs (CLI / JSON / 終了コード)
```

IR は F# record と list / map、判定状態は discriminated union。parser 内の構築用の可変状態は外へ公開しない。rule と差分比較は IR のみを入力にする。Markdown ライブラリへの依存は adapter 内に閉じる。

## 互換性とテスト

`scripts/check.sh`: **59 passed / 0 failed**、ビルドは警告・エラーなし。

- 旧 TypeScript 版で生成した30ケースの期待結果を `tests/DesignLinter.Tests/Fixtures/migration.json` に保存。現在のテストには Node を必要としない。
- 判定、metrics、finding の状態、解析診断、entity / relationship、semantic diff、focus 件数を比較する。旧実装の Markdown 文面や JSON のプロパティ順序の byte 単位の互換性は保証しない。
- 各 rule の正常・違反・判定不能、自己循環／複数循環、未解析領域の差分抑制、証拠行、CLI の入力不変性・終了コードを個別検証。
- 同じ入力からの JSON 再現性、baseline なしの `inputs.baseline: null`、最大3件の focus、AI Review 未実行も検証。
- 小さな実行形式のテスト runner を使用する。実行は `scripts/check.sh`、または `proto run dotnet -- run --project tests/DesignLinter.Tests`。`dotnet test` は使用しない。

CLI の文書化済みオプション、終了コード 0 / 1 / 2 / 3、JSON schemaVersion 1 を維持した。parserVersion は 0.2.0。明示依存の表にある `Component` 列を Component 定義と誤認する問題を修正し、専用の回帰テストを追加した。

旧 TypeScript のソース・テスト・依存設定・node_modules・退避アーカイブは削除済み。回帰テストに必要な期待結果だけを保持する。

## 配布検証

以下は移行時の検証記録。生成したバイナリとレポートは清掃済みで、利用時は publish script で再生成する。

`scripts/publish.sh <RID>` は Release / self-contained / single-file とし、native library を同梱して起動時に展開する。trimming と Native AOT は使用しない。OS・CPU ごとに生成する。

| 対象 | サイズ | 検証 |
|---|---:|---|
| macOS ARM64 | 83,758,862 bytes（約79.9 MiB） | 開発機で実行。DOTNET_ROOT / DOTNET_ROOT_ARM64 を存在しない場所に設定して sample が PASS |
| Linux ARM64 | 84,678,590 bytes（約80.8 MiB） | .NET 未導入の runtime-deps コンテナ内で sample が PASS、差分 COMPARED、focus 3件 |

macOS では sample の起動から JSON 出力まで初回1.067秒、同じ展開先での2回目0.134秒。各1回の実測であり性能保証ではない。macOS ホストには SDK が入っているので、この検証だけを未導入環境での証明とはしない。

Linux は公式 `mcr.microsoft.com/dotnet/runtime-deps` の digest `sha256:099f6f87ed745377dd27bd722f0d1a352bca71b4fddaabfd75e7c064bcaa82da` を使用した。`dotnet` コマンドと `/usr/share/dotnet` が存在しないことを確認したうえで、ネットワークなし・read-only filesystem・`/tmp` のみ tmpfs の環境で実行した。runtime-deps イメージは OS 側の必要ライブラリを含むが、.NET ランタイムは含まない。

再現例（Linux ARM64 を実行できる Docker 環境が必要）:

```sh
scripts/publish.sh linux-arm64
docker run --rm --network none --read-only --tmpfs /tmp \
  -e DOTNET_BUNDLE_EXTRACT_BASE_DIR=/tmp/bundle \
  --mount "type=bind,src=$PWD/artifacts/linux-arm64,dst=/app,readonly" \
  --mount "type=bind,src=$PWD/examples,dst=/examples,readonly" \
  --entrypoint /bin/sh \
  mcr.microsoft.com/dotnet/runtime-deps@sha256:099f6f87ed745377dd27bd722f0d1a352bca71b4fddaabfd75e7c064bcaa82da \
  -c 'if command -v dotnet; then exit 99; fi; test ! -d /usr/share/dotnet || exit 99; /app/design-linter --requirements /examples/payment/after/requirements.md --design /examples/payment/after/design.md --before-requirements /examples/payment/before/requirements.md --before-design /examples/payment/before/design.md --format json'
```

利用者は対応する `design-linter` 実行ファイルを受け取り、そのまま起動する。proto・SDK・Node は開発時だけのツール（Node は移行後の開発にも不要）。native library の展開先には書き込み権限が必要で、`DOTNET_BUNDLE_EXTRACT_BASE_DIR` で指定できる。Linux の OS 依存はこのコンテナの範囲で検証したもので、任意のディストリビューションや musl 環境での動作を保証しない。

公開配布、署名／公証、更新手順、Windows・x64、macOS の .NET 未導入実機は未検証・未実施。ランタイムの修正は再 publish して配布物へ反映する。

次の作業は [roadmap Phase 2](roadmap.md) の EARS profile と決定的なモデル生成。
