using Autodesk.Revit.DB;

namespace HCB.RevitAddin
{
    /// <summary>
    /// Dismisses warnings raised while moving connected MEP elements, so quick click tools do not stop on dialogs.
    /// Errors are left alone and still roll the transaction back.
    /// </summary>
    internal sealed class WarningSwallower : IFailuresPreprocessor
    {
        public static void Attach(Transaction transaction)
        {
            FailureHandlingOptions options = transaction.GetFailureHandlingOptions();
            options.SetFailuresPreprocessor(new WarningSwallower());
            transaction.SetFailureHandlingOptions(options);
        }

        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            foreach (FailureMessageAccessor failure in failuresAccessor.GetFailureMessages())
            {
                if (failure.GetSeverity() == FailureSeverity.Warning)
                {
                    failuresAccessor.DeleteWarning(failure);
                }
            }

            return FailureProcessingResult.Continue;
        }
    }
}
