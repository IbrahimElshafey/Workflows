using Workflows.Definition;

namespace WorkflowSample
{
    /// <summary>
    /// A review/revise cycle modelled as a flat loop over container state instead of recursion.
    /// Bounded recursion is fine, but a review loop that keeps delegating to itself through
    /// WaitSubWorkflow grows the persisted snapshot on every round. Iterating here keeps the
    /// snapshot a fixed size no matter how many rounds the reviewer needs.
    /// </summary>
    [Workflow("ReviewAndReviseWorkflow", 1)]
    public sealed class ReviewAndReviseWorkflow : WorkflowContainer
    {
        public string DraftId { get; set; } = string.Empty;
        public int Revision { get; set; }
        public int MaxRevisions { get; set; } = 3;
        public string LatestFeedback { get; set; } = string.Empty;
        public bool Approved { get; set; }
        public bool Escalated { get; set; }

        public async IAsyncEnumerable<Wait> Run(ReviewAndReviseState state = null!)
        {
            yield return WaitSignal<DraftSubmittedEvent>("DraftSubmitted", "Wait for the first draft")
                .AfterMatch((DraftSubmittedEvent draft) =>
                {
                    DraftId = draft.DraftId;
                    Revision = 1;
                });

            while (Revision <= MaxRevisions)
            {
                yield return WaitSignal<ReviewFeedbackEvent>("ReviewFeedback", "Wait for reviewer feedback")
                    .MatchIf((ReviewFeedbackEvent feedback) => feedback.DraftId == DraftId)
                    .AfterMatch((ReviewFeedbackEvent feedback) =>
                    {
                        LatestFeedback = feedback.Comments;
                        Approved = feedback.Approved;
                    });

                if (Approved)
                {
                    yield break;
                }

                Revision++;

                yield return WaitSignal<DraftRevisedEvent>("DraftRevised", "Wait for the revised draft")
                    .MatchIf((DraftRevisedEvent revised) => revised.DraftId == DraftId)
                    .AfterMatch((DraftRevisedEvent revised) => LatestFeedback = string.Empty);
            }

            Escalated = true;
        }
    }

    public sealed class DraftSubmittedEvent
    {
        public string DraftId { get; set; } = string.Empty;
    }

    public sealed class ReviewFeedbackEvent
    {
        public string DraftId { get; set; } = string.Empty;
        public string Comments { get; set; } = string.Empty;
        public bool Approved { get; set; }
    }

    public sealed class DraftRevisedEvent
    {
        public string DraftId { get; set; } = string.Empty;
    }

    public class ReviewAndReviseState
    {
    }
}