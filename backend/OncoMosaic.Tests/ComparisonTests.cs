using System.IO.Compression;
using System.Text.Json;
using OncoMosaic;
using Xunit;
namespace OncoMosaic.Tests;

public class ComparisonTests
{
    static readonly Thresholds Thresholds = new(.35,.35,.35);
    static CellView Cell(int index,double x,double y,string label) => new(Guid.NewGuid(),index,x,y,12,new {dapi=.7,panck=.8,cd3=.2,cd8=.2},label,label,"ok",[]);
    static ResultSnapshot Snapshot(TissueImage image,Roi roi,List<CellView> cells,int version=0,Thresholds? thresholds=null) => new(
        new AnalysisRun{RoiId=roi.Id,ModelVersion="spectral-mif-sim-v1",AlgorithmVersion="stats-v1"},roi,image,cells,
        Quantification.Calculate(cells,roi,image.PixelSizeUm,5000,thresholds??Thresholds,version),[]);

    [Fact] public void PairsKeepObjectIdentityResolveTiesAndUseReviewedLabels()
    {
        var source=Cell(1,10,10,"cd3-cd8");
        var first=Cell(2,13,14,"panck");
        var second=Cell(3,7,6,"panck");
        var outside=Cell(4,100,10,"panck");
        var roi=new Roi{Width=100,Height=100};
        var cells=new List<CellView>{source,second,first,outside};
        var pair=Assert.Single(Quantification.Neighbors(cells,roi,2));
        Assert.Equal(source.CellId,pair.SourceCellId);Assert.Equal(first.CellId,pair.TargetCellId);
        Assert.Equal(10,pair.DistanceUm);Assert.Equal(13,pair.TargetX);
        var reviewed=cells.Select(c=>c.CellId==first.CellId?c with {EffectiveLabels="excluded"}:c).ToList();
        Assert.Equal(second.CellId,Assert.Single(Quantification.Neighbors(reviewed,roi,2)).TargetCellId);
        Assert.Equal(first.CellId,Assert.Single(Quantification.Neighbors(cells,roi,2)).TargetCellId);
        Assert.Empty(Quantification.Neighbors([source,first with {EffectiveLabels="excluded"},outside],roi,2));
        Assert.Null(Quantification.Calculate([source],roi,2,5000,Thresholds,1).MeanNearestDistanceUm);
    }

    [Fact] public void ComparisonRejectsOtherImagesOrSameRoiAndWarnsOnOverlapsOrSchemeChanges()
    {
        var image=new TissueImage{PixelSizeUm=.5};
        var roi=new Roi{Width=100,Height=100};
        var a=Snapshot(image,roi,[]);
        var b=Snapshot(image,new Roi{X=50,Width=100,Height=100},[],2);
        var comparison=Comparison.Build(image.Id,a,b);
        Assert.True(comparison.SameScheme);Assert.Contains(comparison.Warnings,w=>w.Contains("重叠"));
        Assert.False(Comparison.Build(image.Id,a,b with {Summary=b.Summary with {Thresholds=new(.9,.9,.9)}}).SameScheme);
        Assert.Equal(400,Assert.Throws<ApiError>(()=>Comparison.Build(Guid.NewGuid(),a,b)).Status);
        Assert.Equal(400,Assert.Throws<ApiError>(()=>Comparison.Build(image.Id,a,a)).Status);
    }

    [Fact] public void ExportKeepsPinnedVersionsQuotesNamesAndWritesUncomputableDistanceAsEmpty()
    {
        var image=new TissueImage{PixelSizeUm=.5};
        var a=Snapshot(image,new Roi{Name="区域,\"A\"",Width=100,Height=100},[Cell(1,10,10,"cd3-cd8"),Cell(2,13,14,"panck")],1);
        var b=Snapshot(image,new Roi{Name="B",X=100,Width=100,Height=100},[Cell(1,120,10,"cd3-cd8")],2);
        using var zip=new ZipArchive(new MemoryStream(Comparison.Export(Comparison.Build(image.Id,a,b))));
        Assert.Equal(5,zip.Entries.Count);
        string Read(string name){using var reader=new StreamReader(zip.GetEntry(name)!.Open());return reader.ReadToEnd();}
        using var method=JsonDocument.Parse(Read("method.json"));
        Assert.Equal(1,method.RootElement.GetProperty("a").GetProperty("summary").GetProperty("reviewVersion").GetInt32());
        Assert.Equal(2,method.RootElement.GetProperty("b").GetProperty("summary").GetProperty("reviewVersion").GetInt32());
        Assert.Contains("\"区域,\"\"A\"\"\"",Read("comparison-summary.csv"));
        Assert.EndsWith("\"\",\"0\"\n",Read("comparison-summary.csv"));
        Assert.Equal(2,Read("nearest-neighbors.csv").Split('\n',StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains(a.Cells[0].CellId.ToString(),Read("cells-A.csv"));
    }
}
