using PuzzleBox.Rules;
using PuzzleBox.State;

namespace PuzzleBox.View
{
    public enum PuzzleBoxMessageKind { Blocked, Failure, Success }

    public readonly struct PuzzleBoxFeedbackMessage
    {
        public readonly PuzzleBoxMessageKind Kind;
        public readonly string Title;
        public readonly string Body;
        public PuzzleBoxFeedbackMessage(PuzzleBoxMessageKind kind, string title, string body)
        { Kind = kind; Title = title; Body = body; }

        public static PuzzleBoxFeedbackMessage Blocked(MoveBlockReason reason)
        {
            var title = reason == MoveBlockReason.BoxBlocked || reason == MoveBlockReason.StairBlocked ? "箱子推不动" : "暂时无法移动";
            string body;
            switch (reason)
            {
                case MoveBlockReason.BoxBlocked: body = "箱堆前方或落点没有足够空间。\n请检查上层箱子、其他箱堆和落点高度。"; break;
                case MoveBlockReason.StairBlocked: body = "楼梯出口被箱子挡住了。\n箱子不能沿楼梯推动，请换个方向。"; break;
                case MoveBlockReason.UnsupportedDestination: body = "前方没有可站立的道路。\n试着旋转视角，寻找能够接通的平台。"; break;
                case MoveBlockReason.AmbiguousPerspectiveLink: body = "当前可见出口仍有重复的连接定义。\n请在编辑器中检查该出口的连接配置。"; break;
                case MoveBlockReason.PerspectiveBlocked: body = "前方可见目标没有有效的通路，或入口被遮挡。\n不能穿过它，改走后方被遮住的道路。"; break;
                case MoveBlockReason.Solid: body = "前方被平台或墙体挡住了。\n换个方向再试试。"; break;
                case MoveBlockReason.OutsideGrid: body = "前方已经超出关卡范围。\n换个方向再试试。"; break;
                case MoveBlockReason.MissingPlayer: body = "关卡没有设置猫咪出生点。\n请先在关卡编辑器中放置玩家。"; break;
                default: body = "当前方向暂时无法通行。\n换个方向或旋转视角再试试。"; break;
            }
            return new PuzzleBoxFeedbackMessage(PuzzleBoxMessageKind.Blocked, title, body);
        }

        public static PuzzleBoxFeedbackMessage Failed(FailureReason reason)
        {
            string body;
            switch (reason)
            {
                case FailureReason.BoxHitPlayer: body = "下落的箱子碰到了猫咪。\n可以撤销这一步，重新安排落点。"; break;
                case FailureReason.BoxLandingBlocked: body = "箱堆的落点没有足够空间。\n请检查关卡中悬空箱子的摆放。"; break;
                default: body = "箱子下方没有可以接住它的平台。\n可以撤销这一步，换个方向或视角。"; break;
            }
            return new PuzzleBoxFeedbackMessage(PuzzleBoxMessageKind.Failure, "这次没有成功", body);
        }

        public static PuzzleBoxFeedbackMessage Completed(int moves, int pushes) => new PuzzleBoxFeedbackMessage(
            PuzzleBoxMessageKind.Success, "关卡完成！", "所有箱子都已送达目标位置。\n" + moves + " 次移动  ·  " + pushes + " 次推动\n做得好，猫咪可以休息一下了。");
    }
}
