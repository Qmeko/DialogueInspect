# DialogueInspect

XIVLauncher（Dalamud）向けプラグインです。NPCの会話リストを調べて、選ぶ項目をプリセット保存し、コマンドで自動選択します。

## できること

- ターゲット中のNPCの名前と BaseId を表示する
- `SelectString` / `SelectIconString` / `CutSceneSelectString` / `SelectYesno` の選択肢をタブにする
- タブ内の項目を複数マークしてプリセット保存する
- `/di start 名前` で近くの同じNPCに話しかけ、マークした項目を選ぶ
- アイテム選択時は、通貨の所持数が下限を下回らないように止められる

## まだやらないこと

- 自分でNPCの近くまで歩く（移動の自動化はしません）
- 店で個数を指定して買う専用操作

## ビルド

1. [XIVLauncher](https://goatcorp.github.io/) と Dalamud が入っていること
2. Visual Studio 2022 か `dotnet` があること
3. このフォルダで次を実行する

```powershell
dotnet build "J:\DialogueInspect\DialogueInspect.sln" -c Release
```

できた DLL の場所:

```
J:\DialogueInspect\DialogueInspect\bin\x64\Release\DialogueInspect.dll
```

## ゲームへの入れ方（最初の1回）

Dev Plugin は使いません。[Qmeko/DalamudPlugins](https://github.com/Qmeko/DalamudPlugins) から入れます。

1. ゲームを XIVLauncher で起動する
2. チャットに `/xlsettings` と打つ
3. **Experimental**（試験的機能）タブを開く
4. **Custom Plugin Repositories** に、次の URL を追加して有効にする

```
https://raw.githubusercontent.com/Qmeko/DalamudPlugins/refs/heads/main/pluginmaster.json
```

5. 保存したあと `/xlplugins` を開き、DialogueInspect をインストールする

以前 Dev Plugin Locations に `J:\DialogueInspect\...` を入れている場合は、その行を消してください。同じプラグインが二重に出ます。

## 使い方

1. `/di` で窓を開く
2. NPCをターゲットして **今のターゲットを親にする**
3. 自分で1回話しかける。選択リストが出るたびにタブが増える
4. 後で選びたい行にチェックを入れる（複数可）
5. 名前を付けて **保存**
6. NPCの近くで `/di start 名前`
7. 止めるときは `/di stop`

### コマンド

| コマンド | 意味 |
|---|---|
| `/di` | 窓の開閉 |
| `/di start [名前]` | プリセット開始。名前を省略すると、窓で選んでいるもの |
| `/di stop` | 停止 |
| `/di list` | プリセット一覧 |
| `/di save [名前]` | 今の内容を保存 |
| `/di status` | 動いているか確認 |

開始時は **1回だけ** が初期値です。チェックを外すと、`/di stop` まで繰り返します。
