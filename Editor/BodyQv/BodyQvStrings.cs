namespace Maaaaa.EXQv.Editor
{
    internal static class BodyQvStrings
    {
        public const string MissingPens = "対象ペンが指定されていません。";
        public const string NullPen = "対象ペンに None が含まれています。";
        public const string MissingLateSync = "インクプールを特定できない対象ペンがあります。対象を指定し直してください。";
        public const string InvalidLayer = "体コライダーのレイヤーは 22～31 のユーザーレイヤーにしてください。";
        public const string PlayerCollision = "体コライダーが Player または PlayerLocal と衝突します。プレイヤーが押されないように衝突を無効にしてください。";
        public const string PickupCollision = "体コライダーが対象ペンの Pickup レイヤーと衝突しません。表面吸着のために衝突を有効にしてください。";
        public const string SurftraceMask = "対象ペンの surftraceMask に体コライダーのレイヤーが含まれていません。";
        public const string QvPenVersion = "動作確認済みの QvPen は 3.3.15 です。現在のバージョン: {0}";
        public const string FixPlayerCollision = "Player との衝突を直す";
        public const string FixPickupCollision = "Pickup との衝突を直す";
        public const string FixSurftraceMask = "surftraceMask に追加";
        public const string AddScenePens = "シーン内の QvPen を一覧から追加";
        public const string ConfirmTitle = "BodyQv のレイヤー設定";
        public const string ConfirmPlayerCollision = "体コライダーのレイヤー {0} と Player / PlayerLocal の衝突を無効にします。\n\nほかのレイヤー同士の設定は変更しません。";
        public const string ConfirmPickupCollision = "体コライダーのレイヤー {0} と、対象ペンの Pickup レイヤーの衝突を有効にします。\n\nほかのレイヤー同士の設定は変更しません。";
        public const string Apply = "変更する";
        public const string Cancel = "キャンセル";
        public const string PickerTitle = "対象にする QvPen";
        public const string PickerEmpty = "シーン内に QvPen_PenManager がありません。";
        public const string AddSelected = "選択したペンを追加";
    }
}
