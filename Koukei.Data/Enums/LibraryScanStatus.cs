namespace Koukei.Data.Enums;

public enum LibraryScanStatus
{
    NeverScanned = 0,
    Running = 1,
    Completed = 2,
    CompletedWithErrors = 3,
    Failed = 4,
    Canceled = 5
}
