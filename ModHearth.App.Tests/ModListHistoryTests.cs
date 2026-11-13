using ModHearth.Models;
using ModHearth.UI.ModList;
using Xunit;

namespace ModHearth.App.Tests
{
    public class ModListHistoryTests
    {
        [Fact]
        public void UndoAndRedoWalkTheRecordedStates()
        {
            ModListHistory history = new();
            DfhMod first = Mod(1);
            DfhMod second = Mod(2);
            DfhMod third = Mod(3);

            history.Reset([first]);
            Assert.True(history.Record([first, second]));
            Assert.True(history.Record([first, second, third]));

            Assert.True(history.CanUndo);
            Assert.False(history.CanRedo);
            Assert.Equal(new[] { first, second }, history.Undo());
            Assert.True(history.CanRedo);
            Assert.Equal(new[] { first }, history.Undo());
            Assert.False(history.CanUndo);
            Assert.Null(history.Undo());

            Assert.Equal(new[] { first, second }, history.Redo());
            Assert.Equal(new[] { first, second, third }, history.Redo());
            Assert.False(history.CanRedo);
        }

        [Fact]
        public void RecordingAfterUndoDropsTheRedoBranch()
        {
            ModListHistory history = new();
            DfhMod first = Mod(1);
            DfhMod second = Mod(2);
            DfhMod replacement = Mod(3);

            history.Reset([first]);
            Assert.True(history.Record([first, second]));
            Assert.Equal(new[] { first }, history.Undo());
            Assert.True(history.CanRedo);

            Assert.True(history.Record([replacement]));
            Assert.False(history.CanRedo);
            Assert.Equal(new[] { first }, history.Undo());
        }

        [Fact]
        public void IdenticalConsecutiveStatesDoNotConsumeHistory()
        {
            ModListHistory history = new();
            DfhMod first = Mod(1);
            DfhMod second = Mod(2);

            history.Reset([first, second]);

            Assert.False(history.Record([Mod(1), Mod(2)]));
            Assert.False(history.CanUndo);
        }

        [Fact]
        public void KeepsOnlyFiveUndoStates()
        {
            ModListHistory history = new();
            history.Reset([Mod(0)]);

            for (int i = 1; i <= 10; i++)
                Assert.True(history.Record([Mod(i)]));

            int undoCount = 0;
            while (history.Undo() != null)
                undoCount++;

            Assert.Equal(ModListHistory.MaxUndoDepth, undoCount);
        }

        [Fact]
        public void SnapshotsDoNotAliasTheSourceList()
        {
            ModListHistory history = new();
            DfhMod first = Mod(1);
            DfhMod second = Mod(2);
            List<DfhMod> source = [first, second];

            history.Reset(source);
            Assert.True(history.Record([first]));
            source.Clear();

            Assert.Equal(new[] { first, second }, history.Undo());
        }

        [Fact]
        public void ClearRemovesBothUndoAndRedoStates()
        {
            ModListHistory history = new();
            history.Reset([Mod(1)]);
            Assert.True(history.Record([Mod(2)]));
            _ = history.Undo();

            history.Clear();

            Assert.False(history.CanUndo);
            Assert.False(history.CanRedo);
            Assert.Null(history.Undo());
            Assert.Null(history.Redo());
        }

        private static DfhMod Mod(int id) => new($"mod{id}", 1);
    }
}
