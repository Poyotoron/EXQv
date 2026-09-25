namespace Maaaaa.EXQv
{
    public static class GrabQvStrings
    {
        public const string TargetPensHeader = "対象ペン";
        public const string TargetPensTooltip = "描いた線を持てるようにする QvPen の PenManager です。";
        public const string AutoSplitDistanceLabel = "自動で区切る距離（m）";
        public const string AutoSplitDistanceTooltip = "線の全点が現在の持ち手からこの距離以上離れていると、新しいまとまりにします。0 で無効です。";
        public const string LogResultsLabel = "結果をログに出す（確認用）";
        public const string PickupText = "持つ";
        public const string CreatedLog = "[GrabQv] オブジェクトを作成: ";
        public const string AssignedLog = "[GrabQv] 持ち手を割り当て: ";
        public const string ReleasedLog = "[GrabQv] 持ち手を空きに戻しました: ";
        public const string FullLog = "[GrabQv] 空いている持ち手がありません: ";
        public const string SplitButtonText = "Split\n(Global)";
        public const string AddScenePens = "シーンの QvPen から対象を追加";
        public const string PickerTitle = "GrabQv 対象ペン";
        public const string PickerEmpty = "シーン内に QvPen の PenManager が見つかりません。";
        public const string AddSelected = "選択したペンを追加";
        public const string MissingPens = "対象ペンが設定されていません。";
        public const string NullPen = "対象ペンに null が含まれています。";
        public const string MissingLateSync = "対象ペンの LateSync が見つかりません。QvPen の構成を確認してください。";
        public const string BodyQvOverlap = "BodyQv と GrabQv の両方に指定されたペンがあります。線の追従が競合するため、片方から外してください。";
        public const string MissingSplitButtons = "区切りボタンが無い対象ペンがあります。";
        public const string CreateSplitButtons = "区切りボタンを作る";
        public const string ExtraSplitButtons = "対象から外したペンの区切りボタンが残っています。";
        public const string RemoveExtraSplitButtons = "不要な区切りボタンを消す";
        public const string QvPenVersion = "対応確認済みの QvPen は 3.3.15 です。現在: {0}";
    }
}
