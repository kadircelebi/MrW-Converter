using MrWConverter.Core.Output;

namespace MrWConverter.Core.Tests;

public class OutputBatchTests
{
    [Fact]
    public void Commit_PublishesFilesAndRemovesStaging()
    {
        using var folder = new TemporaryFolder();

        using (var batch = new OutputBatch(folder.Path))
        {
            File.WriteAllText(batch.Stage("a.png"), "a");
            var published = batch.Commit();

            Assert.Equal([folder.File("a.png")], published);
        }

        Assert.Equal(["a.png"], folder.Entries());
    }

    [Fact]
    public void Commit_NeverOverwritesAnExistingFile()
    {
        using var folder = new TemporaryFolder();
        File.WriteAllText(folder.File("a.png"), "original");

        using (var batch = new OutputBatch(folder.Path))
        {
            File.WriteAllText(batch.Stage("a.png"), "new");
            batch.Commit();
        }

        Assert.Equal("original", File.ReadAllText(folder.File("a.png")));
        Assert.Equal("new", File.ReadAllText(folder.File("a (2).png")));
    }

    [Fact]
    public void Commit_KeepsFilesWithTheSameNameInOneJobApart()
    {
        using var folder = new TemporaryFolder();

        using (var batch = new OutputBatch(folder.Path))
        {
            File.WriteAllText(batch.Stage("a.png"), "first");
            File.WriteAllText(batch.Stage("a.png"), "second");
            batch.Commit();
        }

        Assert.Equal("first", File.ReadAllText(folder.File("a.png")));
        Assert.Equal("second", File.ReadAllText(folder.File("a (2).png")));
    }

    [Fact]
    public void Dispose_WithoutCommit_LeavesNothingBehind()
    {
        using var folder = new TemporaryFolder();

        using (var batch = new OutputBatch(folder.Path))
        {
            File.WriteAllText(batch.Stage("a.png"), "a");
        }

        Assert.Empty(folder.Entries());
    }

    [Fact]
    public void Commit_WithAnEmptyOutput_PublishesNothing()
    {
        using var folder = new TemporaryFolder();

        using (var batch = new OutputBatch(folder.Path))
        {
            File.WriteAllText(batch.Stage("good.png"), "a");
            File.WriteAllText(batch.Stage("empty.png"), "");

            Assert.Throws<InvalidDataException>(() => batch.Commit());
        }

        Assert.Empty(folder.Entries());
    }

    [Fact]
    public void Stage_RejectsAPathInsteadOfAName()
    {
        using var folder = new TemporaryFolder();
        using var batch = new OutputBatch(folder.Path);

        Assert.Throws<ArgumentException>(() => batch.Stage(@"..\a.png"));
    }
}
