# スタミナ変更のカバレッジ

2026-11-15、Microsoft.CodeCoverage の Cobertura、Release / .NET 10。対象を絞った 22 テストが全件成功。

| 対象 | 実行した行 / 実行可能行 | 行カバレッジ |
|---|---:|---:|
| 新規 `Fighter.Stamina.cs`（`MuscleWork` を含む）全体 | 121 / 121 | 100 % |
| MiniRing 本体の変更した実行可能行 | 158 / 160 | 98.75 % |

変更行は `git diff --unified=0` の追加範囲と Cobertura の行を突き合わせた。新規ファイルは全体。コメント・空行・コンパイラーが計測対象としない宣言は分母に含めない。もともと作業ツリーにあった変更と重なる `BoxerBot.cs` の差分も含むため、独立したタスク基準コミットからの厳密な差分ではない。

`MiniRing.Arena/StaminaLab.cs` の `off` 変種の 1 行はテストプロジェクトの参照範囲外で、計測対象外。CLI の `staminalab --variants off,on,pace` で実行している。これを未実行 1 行として保守的に加えても 158 / 161 = **98.14 %**。

実行したコマンド（既にビルドした、他の検証と独立した出力先）:

```powershell
dotnet test tests/MiniRing.Tests -c Release --artifacts-path .stamina-build --no-build --filter "FullyQualifiedName!~Probe&(FullyQualifiedName~StaminaBudgetTests|FullyQualifiedName~StaminaRuleTests|FullyQualifiedName~StaminaRestDecisionTests|FullyQualifiedName~TwoHanded|FullyQualifiedName~TheArmAboveTheBreak|FullyQualifiedName~Wrist|FullyQualifiedName~BodyToFistTests)" --collect "Code Coverage;Format=cobertura" --results-directory .testresults/stamina-coverage --logger "console;verbosity=normal"
```

生の XML（git の外）:
`.testresults/stamina-coverage/19c9e08f-8ddd-4204-89fe-dbbcf8bf93bb/smiya_GRANDEDESKTOP_2026-11-15.11_48_05.cobertura.xml`

全テストの結果は [gen13_stamina.md](gen13_stamina.md) に記載する。上記の 22 件はカバレッジ用の実行で、全テストの代用ではない。
