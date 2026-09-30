# サンプル

`payment/` は、この PoC のために作成した小さな fixture です。構造は cc-sdd revision `e2a0c671aef37a482404905b409ab5c27092b25b` の template / rules を参照しています。実運用の設計の品質や cc-sdd review gate 全体の通過を示すものではありません。

- `before/requirements.md` と `before/design.md`: 変更前。PaymentService が記録と retry の両方を担当。
- `after/requirements.md` と `after/design.md`: 変更後。retry を PaymentRetryWorker に移管。要件は変更なし。

`before/` と `after/` はこのサンプル独自の比較用ディレクトリです。cc-sdd の出力形式ではありません。実際の比較元は Git 履歴などから取得します。比較元なしでも現在の `requirements.md` と `design.md` の検査は実行できます。

## 実行

リポジトリルートで実行します。

```sh
proto run dotnet -- run --project src/DesignLinter -- --requirements examples/payment/after/requirements.md \
  --design examples/payment/after/design.md \
  --before-requirements examples/payment/before/requirements.md \
  --before-design examples/payment/before/design.md
```

期待される結果:

- Mechanical PASS。要件対応 2/2、Component–File mapping 2/2。
- PaymentRetryWorker と `src/payment/retry.ts` の追加。
- PaymentRetryWorker → PaymentService の依存追加。
- 要件 1.2 の対応先変更。
- Human Review Focus は要件解釈、依存境界、新しい Component の責務に関する3問。
- AI Review は NOT_RUN。

`--format json` を付けると、`designIR` から dependency graph と evidence も確認できます。
