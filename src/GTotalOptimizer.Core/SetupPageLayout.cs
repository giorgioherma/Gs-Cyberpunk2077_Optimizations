namespace GTotalOptimizer.Core;

// Pixel layout contract at 100% scaling for the fixed, non-scrollable setup page.
// WinForms scales these bounds with DPI. The form prevents shrinking below the
// declared minimum client size; Page 2 remains independently scrollable.
public static class SetupPageLayout
{
    public const int ReferenceClientHeight = 790;
    public const int LayoutTopPadding = 7;
    public const int LayoutBottomPadding = 7;
    public const int HeaderHeight = 60;
    public const int ActionsHeight = 52;
    public const int StatusHeight = 38;
    public const int NormalSourceHeight = 166;
    public const int Mo2SourceHeight = 241;
    public const int InstructionsHeight = 272;
    public const int InstructionLastTextBottom = 261;
    public const int Mo2EngineNoticeTop = 174;
    public const int Mo2ChecksTop = 194;
    public const int Mo2StatusTop = 215;
    public const int MinimumWorkflowHeight = 132;

    public static int AvailableContentHeight(int clientHeight) =>
        clientHeight - LayoutTopPadding - LayoutBottomPadding
        - HeaderHeight - ActionsHeight - StatusHeight;

    public static int WorkflowHeight(int clientHeight, bool mo2) =>
        AvailableContentHeight(clientHeight) - (mo2 ? Mo2SourceHeight : NormalSourceHeight)
        - InstructionsHeight;

    public static bool FitsWithoutScroll(int clientHeight, bool mo2) =>
        WorkflowHeight(clientHeight, mo2) >= MinimumWorkflowHeight;
}
