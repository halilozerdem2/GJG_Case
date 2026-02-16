public class WaitingState : BaseLilState
{
    public override LilStateId Id => LilStateId.Waiting;
    public override bool IsOneShot => false;
}
