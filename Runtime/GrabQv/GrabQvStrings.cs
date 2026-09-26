namespace Maaaaa.EXQv
{
    public static class GrabQvStrings
    {
        public const string TargetPensHeader = "対象ペン";
        public const string TargetPensTooltip = "描いた線を持てるようにする QvPen の PenManager です。";
        public const string AutoSplitDistanceLabel = "自動で区切る距離（m）";
        public const string AutoSplitDistanceTooltip = "線の全点が現在の持ち手からこの距離以上離れていると、新しいまとまりにします。0 で無効です。";
        public const string LogResultsLabel = "結果をログに出す（確認用）";
        public const string ToggleGrabLabel = "押すたびに持つ／離す";
        public const string ToggleGrabTooltip = "ON: 押すと持ち、もう一度押すと離します（ペンと同じ持ち方）。OFF: 押している間だけ持ちます。";
        public const string PickupText = "持つ";
        public const string CreatedLog = "[GrabQv] オブジェクトを作成: ";
        public const string AssignedLog = "[GrabQv] 持ち手を割り当て: ";
        public const string ReleasedLog = "[GrabQv] 持ち手を空きに戻しました: ";
        public const string FullLog = "[GrabQv] 空いている持ち手がありません: ";
        public const string CurrentObjectClearedLog = "[GrabQv] 今のオブジェクトを外しました: オブジェクト ";
        public const string PenLabel = " / ペン ";
        public const string SplitButtonText = "Split\n(Global)";
        public const string AddScenePens = "シーンの QvPen から対象を追加";
        public const string PickerTitle = "GrabQv 対象ペン";
        public const string PickerEmpty = "シーン内に QvPen の PenManager が見つかりません。";
        public const string AddSelected = "選択したペンを追加";
        public const string MissingPens = "対象ペンが設定されていません。";
        public const string NullPen = "対象ペンに null が含まれています。";
        public const string MissingLateSync = "対象ペンの LateSync が見つかりません。QvPen の構成を確認してください。";
        public const string BodyQvCombined = "次のペンは BodyQv の対象でもあるため、組み合わせて動きます（体に追従し、持って位置を直せます）。";
        public const string MultipleBodyQvManagers = "対象ペンを共有する BodyQvManager が複数あります。組み合わせ先を決められないため、BodyQvManager への参照を設定できません。";
        public const string DrawnReason = "描いた";
        public const string DroppedReason = "離した";
        public const string HeldLog = "[GrabQv] 持ちました: まとまり ";
        public const string DroppedLog = "[GrabQv] 離しました: まとまり ";
        public const string PlayerLeftLog = "[GrabQv] プレイヤー退出のため最後の位置に固定: まとまり ";
        public const string BindingResultPrefix = "[GrabQv] まとまり ";
        public const string MissingSplitButtons = "区切りボタンが無い対象ペンがあります。";
        public const string CreateSplitButtons = "区切りボタンを作る";
        public const string ExtraSplitButtons = "対象から外したペンの区切りボタンが残っています。";
        public const string RemoveExtraSplitButtons = "不要な区切りボタンを消す";
        public const string MissingEraseButtons = "Erase ボタンがまだ差し替えられていない対象ペンがあります。";
        public const string ReplaceEraseButtons = "Erase ボタンを差し替える";
        public const string ExtraEraseButtons = "対象外・管理不明・重複している差し替え済み Erase ボタンがあります。";
        public const string RestoreEraseButtons = "不要・重複した Erase ボタンを元に戻す";
        public const string MissingEraseReferences = "QvPen の Erase ボタンの表示参照を取得できません。QvPen の構成を確認してください。";
        public const string EraseSplitUiName = "EraseUI (Split)";
        public const string EraseSplitIndicatorName = "Indicator (Split)";
        public const string EraseSplitTextName = "Text (Split)";
        public const string EraseSplitText = "SPLIT";
        public const string EraseStartedLog = "[GrabQv] まとまりを消去: まとまり ";
        public const string EraseInkCountLog = " / 線 ";
        public const string EraseOtherCountLog = " / 他人の線 ";
        public const string EraseStoppedLog = "[GrabQv] まとまりの消去を中止: ";
        public const string EraseStoppedHeld = "ペンを持ったため";
        public const string EraseStoppedOwner = "ペンのオーナーが替わったため";
        public const string EraseStoppedTimeout = "線の消去が 10 秒以内に受け付けられなかったため";
        public const string EraseStoppedSelf = "Self を実行するため";
        public const string EraseStoppedAll = "All を実行するため";
        public const string QvPenVersion = "対応確認済みの QvPen は 3.3.15 です。現在: {0}";
    }
}
