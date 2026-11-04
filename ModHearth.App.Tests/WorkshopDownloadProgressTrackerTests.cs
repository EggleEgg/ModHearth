using ModHearth.Utilities.Workshop;
using Xunit;

namespace ModHearth.App.Tests
{
    public class WorkshopDownloadProgressTrackerTests
    {
        [Fact]
        public void ReportsAggregateProgressForSingleDownload()
        {
            WorkshopDownloadProgressTracker tracker = new();
            using WorkshopDownloadSession session = tracker.BeginSession();

            long entry = session.Track();
            session.Report(entry, 42);

            Assert.Equal(1, tracker.ActiveDownloadCount);
            Assert.Equal(42, tracker.OverallProgress, 3);
            Assert.Contains("1 workshop mod", tracker.StatusText);

            session.Complete(entry);

            Assert.Equal(0, tracker.ActiveDownloadCount);
            Assert.Equal(0, tracker.OverallProgress, 3);
            Assert.Equal(string.Empty, tracker.StatusText);
        }

        [Fact]
        public void CompletedEntryStaysCountedUntilRunEnds()
        {
            WorkshopDownloadProgressTracker tracker = new();
            using WorkshopDownloadSession session = tracker.BeginSession();

            long first = session.Track();
            long second = session.Track();
            session.Report(second, 50);
            session.Complete(first);

            // Completed downloads count as 100% so the aggregate keeps moving forward.
            Assert.Equal(1, tracker.ActiveDownloadCount);
            Assert.Equal(75, tracker.OverallProgress, 3);

            session.Complete(second);

            Assert.Equal(0, tracker.ActiveDownloadCount);
            Assert.Equal(0, tracker.OverallProgress, 3);
        }

        [Fact]
        public void DroppedEntryNoLongerAffectsAggregate()
        {
            WorkshopDownloadProgressTracker tracker = new();
            using WorkshopDownloadSession session = tracker.BeginSession();

            long kept = session.Track();
            long dropped = session.Track();
            session.Report(kept, 10);
            session.Report(dropped, 90);

            session.Drop(dropped);

            Assert.Equal(1, tracker.ActiveDownloadCount);
            Assert.Equal(10, tracker.OverallProgress, 3);
        }

        [Fact]
        public void DisposingSessionOnlyRemovesItsOwnEntries()
        {
            WorkshopDownloadProgressTracker tracker = new();

            using (WorkshopDownloadSession first = tracker.BeginSession())
            {
                long firstEntry = first.Track();
                first.Report(firstEntry, 20);
            }

            using WorkshopDownloadSession second = tracker.BeginSession();
            long secondEntry = second.Track();
            second.Report(secondEntry, 60);

            Assert.Equal(1, tracker.ActiveDownloadCount);
            Assert.Equal(60, tracker.OverallProgress, 3);
        }

        [Fact]
        public void ChangedFiresWhenAggregateMoves()
        {
            WorkshopDownloadProgressTracker tracker = new();
            int changes = 0;
            tracker.Changed += (_, _) => changes++;

            using WorkshopDownloadSession session = tracker.BeginSession();
            long entry = session.Track();
            session.Report(entry, 30);
            session.Complete(entry);

            Assert.True(changes >= 3);
        }
    }
}
