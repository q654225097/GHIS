namespace HIS.Client.UrsaShell.Shell;

public interface IStationLifecycle
{
    void Activate();

    void Deactivate();

    void Dispose();
}
