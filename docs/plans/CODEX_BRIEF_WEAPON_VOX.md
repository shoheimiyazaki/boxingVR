# 指示書: 武器・はめる物のボクセルを .vox で書き出し・確かめ・組み込む

体（ボクサー）には既に `voxexport` / `voxcheck` がある（`docs/VOXEL_EDITING.md`、`games/MiniRing.Arena/VoxelTool.cs`、
`games/MiniRing/Voxels/BoxerVoxelModel.cs`、テスト `tests/MiniRing.Tests/VoxelModelFileTests.cs`）。
同じことを武器・はめる物（`games/MiniRing/Weapons/Weapon.cs` の `WeaponKind`）にもできるようにする。
ユーザーは書き出した .vox を Vengi / MagicaVoxel で直して、ゲームへ戻したい。

**作業ツリーには他の未コミットの変更が多くある。下に挙げたファイル以外は触らない。コミットしない。**

## 決めたこと（変えない）

1. **対象**: `IronSword, WoodenClub, Katana, Longsword, Glove, KnuckleDuster, Katar, Gauntlet`。
   **`Fist` は対象外**: `Weapon` のコンストラクタで `BareHandState.Build(HandShape.Seiken)` に置き換わり、`BareHandState` は
   `Weapon.Build(WeaponKind.Fist)` の生成の幾何から指・節を振り分ける（手形もセルの幾何変換）。直しを通すと振り分けが合わなくなる。
   書き出しにも含めない。読み込みで `Fist` という名前があったらエラー（理由を添える）。docs に理由を書く。
2. **新しいファイル** `games/MiniRing/Voxels/WeaponVoxelModel.cs`（体の `BoxerVoxelModel.cs` に倣う）:
   - `WeaponVoxelModel.Editable`（対象の種類の一覧）、`ToVox(IReadOnlyDictionary<WeaponKind, VoxelGrid>)`、`FromVox(VoxScene)`、
     `Parts(WeaponVoxelOverrides? overrides)`（今の中身。赤コーナーの革）。
   - `WeaponVoxelOverrides`（体の `BoxerVoxelOverrides` に倣う）: `Shipped`（埋め込みリソース `MiniRing.Voxels.weapons.vox`、無ければ null）、
     `Load(path)`、`For(kind, dims)`（大きさが違えば null を返して 1 回だけ警告。体と同じ方針）。
   - パレット・色 ↔ 材質は体と同じ `BoxerVoxelModel.ColorOf` / 色で引く仕組みを使い回す（二重に書かない。必要なら共通部分を小さく取り出す）。
   - 軸: 体と同じ変換（.vox は Z が上）。武器のローカルは +X が切っ先・Y が刃の幅・Z が厚さ。.vox では X=長さのまま、Z を上に Y を厚さにするなど、
     体の `ToVoxAxes` と同じ規則で良い（行って戻れば同じであること）。
3. **256 を超える武器の分割**: .vox の 1 モデルは各軸 256 セルまで（`MagicaVox.MaxSize`。XYZI の座標は 1 バイト）。
   剣は 264、ロングソードは 304 セル（長さ方向）。256 を超える軸は 256 以下の塊に分け、モデル名を `IronSword#0`, `IronSword#1` …
   とし、nTRN の `_t`（`MagicaVox.cs` の `Transform` に translation がある。必要なら `VoxModel` に位置を持たせて書き・読みする）で
   エディタ上で 1 本につながって見えるよう並べる。読み込みでは同じ種類の塊を番号順に結合して 1 本に戻す（塊の欠け・重複・大きさの不整合はエラー）。
   256 以下の武器は `#` を付けず種類名だけ。体の .vox の読み書きを壊さないこと（既存テストが通ること）。
4. **革**: `Glove`・`Gauntlet` は赤い革（`BoxerAnatomy.RedGlove` = 18）で書き出す。読み込みでは青い革（19）も 18 に戻す。
   `Weapon.Build(kind, leather)` で直しを使うときは 18 を `leather` に置き換える（体のトランクスと同じ扱い）。
5. **ゲームへ通す**: `Weapon.Build(kind, leather)` で、`WeaponVoxelOverrides.Shipped?.For(kind, dims)` があれば生成の代わりにそれを使う
   （大きさ = 生成した格子と同じセルの数・セルの大きさ・半分の大きさ）。`KarateFist` の外側を皮膚にする後処理のような、生成に付いた後処理が
   対象の種類にあれば、直しにも同じ結果になるか確かめ、どちらにするか docs に書く。直しが無ければ今と全く同じ格子（物理・再生が変わらない）。
6. **埋め込み**: `games/MiniRing/MiniRing.csproj` に
   `<EmbeddedResource Include="Voxels\weapons.vox" LogicalName="MiniRing.Voxels.weapons.vox" Condition="Exists('Voxels\weapons.vox')" />`。
   `weapons.vox` はまだ置かない（組み込みはユーザーが直してから）。
7. **Arena**: `games/MiniRing.Arena/Program.cs` に `weaponexport --out weapons.vox [--from current|generated]` と
   `weaponcheck --in weapons.vox [--install yes]` を足す（冒頭の使い方のコメントにも）。`VoxelTool.cs` に `ExportWeapons` / `CheckWeapons` を足す
   （出力は体と同じ形: 種類ごとの大きさ、塊の数、材質ごとの変化。`--install yes` は `games/MiniRing/Voxels/weapons.vox` へコピー）。
8. **版**: 道具を足すだけなので `Fighter.PhysicsVersion` は上げない。docs に「直した weapons.vox を組み込むと武器の質量・接触が変わるので、
   組み込むときに `PhysicsVersion` を上げる」と書く。

## テスト（`tests/MiniRing.Tests/WeaponVoxelModelFileTests.cs` を新しく。`VoxelModelFileTests.cs` に倣う）

- 全対象の種類を書き出して（ストリームへ）読み戻すと、どの種類も生成と材質がセル単位で一致する。
- 256 を超える種類（剣・ロングソード）は 2 つ以上の塊に分かれ、どの塊も各軸 256 以下、読み戻すと 1 本の大きさに戻る。
- 青い革（19）に塗り替えたグローブを読み戻すと 18 になり、`Build(Glove, BlueGlove)` 相当で青になる。
- 大きさの違う直しは使われず生成に戻る（`For` が null）。
- 直しがあると `Build` がそれを使う（1 セルを鉄から木に変えた直しで確かめる。`Shipped` に頼らずに `Build` へ直しを渡せる内部の口を作ってよい）。
- `Fist` という名前・知らない名前・材質に無い色はエラー。
- 既存の `VoxelModelFileTests` がそのまま通る。

## 文書

- `docs/VOXEL_EDITING.md` に「武器・はめる物」の節: コマンド、対象と `Fist` を外した理由、塊の名前と並べ方（直すときは塊をまたいで塗ってよいが、
  大きさ・位置を変えない）、革の色、組み込むと `PhysicsVersion` を上げること、材質の番号表に 16 鉄・17 木・18 赤い革・19 青い革・20 詰め物・21 唇を足す。
- `CLAUDE.md` §5 の表の「ボクセルの書き出し・確かめ」の行に `weaponexport` / `weaponcheck` を足す（その 1 行だけ）。
- `README.md` のテスト件数（今 553 件）を、増えた件数に合わせる。

## 完了の条件と確かめ方

1. `dotnet build` が警告なしで通る（既存の警告は増やさない）。
2. `dotnet test tests/MiniRing.Tests --filter "FullyQualifiedName~WeaponVoxel|FullyQualifiedName~VoxelModelFile"` が通る。
3. 全テスト `dotnet test tests/MiniRing.Tests --filter "FullyQualifiedName!~Probe"` が通る（時間がかかるので最後に 1 回。落ちたら自分の変更が原因か確かめる。
   他の未コミットの変更のせいで落ちるものは、名前と理由を報告するだけで直さない）。
4. `dotnet run --project games/MiniRing.Arena -c Release -- weaponexport --out media/vox/weapons.vox` →
   `weaponcheck --in media/vox/weapons.vox` で全種類 unchanged。
5. 報告: 変えたファイル、テストの件数（新規と全体）、全テストの結果、決めかねて選んだ所。
