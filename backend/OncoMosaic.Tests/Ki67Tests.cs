using System.IO.Compression;
using OncoMosaic;
using Xunit;
namespace OncoMosaic.Tests;

public class Ki67Tests
{
    static readonly Thresholds T = new(.35,.35,.35,.35);
    static Cell Cell(double? value, string quality="ok", bool tCell=false) => new() {X=10,Y=10,AreaPx=20,PanckValue=tCell ? .01 : .8,Cd3Value=tCell ? .8 : .01,Cd8Value=tCell ? .8 : .01,Ki67Value=value,Ki67Quality=value==null?"not-measured":quality,Ki67ValidPixelCount=value==null?0:20};
    static Summary Summary(List<CellView> cells, int version=0) => Quantification.Calculate(cells,new Roi {Width=100,Height=100},1,5000,T,version);
    [Fact] public void IdentityAndNuclearStateAreIndependent()
    {
        var epithelial=Cell(.6);var t=Cell(.7,tCell:true);
        var views=Quantification.Views([epithelial,t],T,new Dictionary<Guid,string>());
        Assert.Equal("panck",views[0].EffectiveLabels);Assert.Equal("cd3-cd8",views[1].EffectiveLabels);
        Assert.All(views,c=>Assert.Equal("positive",c.Ki67!.EffectiveState));
        Assert.Equal(1,Summary(views).Ki67!["cd3"].PositiveCount);
        Assert.Equal(1,Summary(views).Ki67!["cd3-cd8"].PositiveCount);
        Assert.Equal("positive",Ki67Quantification.View(Cell(.35),T,null).AutoState);
    }
    [Fact] public void UnknownAndMissingAreNotNegativeAndReviewKeepsHistory()
    {
        var cells=Enumerable.Range(0,30).Select(_=>Cell(.8)).Concat(Enumerable.Range(0,60).Select(_=>Cell(.1))).Concat(Enumerable.Range(0,10).Select(_=>Cell(.8,"spectral-residual"))).ToList();
        var original=Quantification.Views(cells,T,new Dictionary<Guid,string>());
        var q=Summary(original).Ki67!["panck"];
        Assert.Equal(90,q.EvaluableCount);Assert.Equal(100,q.TargetCount);Assert.Equal(1.0/3,q.Fraction);Assert.Equal(.9,q.Coverage);
        var reviewed=Quantification.Views(cells,T,new Dictionary<Guid,string>(),new Dictionary<Guid,string>{{cells[0].Id,"negative"}});
        Assert.Equal(29.0/90,Summary(reviewed,1).Ki67!["panck"].Fraction);
        var excluded=Quantification.Views(cells,T,new Dictionary<Guid,string>{{cells[0].Id,"excluded"}});
        Assert.Equal(29.0/89,Summary(excluded,2).Ki67!["panck"].Fraction);
        Assert.Equal(1.0/3,Summary(original).Ki67!["panck"].Fraction);
        Assert.Equal("positive",reviewed[0].Ki67!.AutoState);
    }
    [Fact] public void MissingQcFailureEmptyAndZeroAreDistinct()
    {
        var q=Summary(Quantification.Views([Cell(null)],T,new Dictionary<Guid,string>())).Ki67!["panck"];
        Assert.Null(q.Fraction);Assert.Equal("not-measured",q.Status);Assert.Equal(1,q.NotMeasuredCount);
        Assert.Equal("not-measured",Ki67Quantification.View(Cell(null),T,"positive").EffectiveState);
        Assert.Equal("indeterminate",Ki67Quantification.View(Cell(.9,"channel-qc-failed"),T,"positive").EffectiveState);
        Assert.Null(Summary([]).Ki67!["panck"].Fraction);
        Assert.Equal(0,Summary(Quantification.Views([Cell(.1)],T,new Dictionary<Guid,string>())).Ki67!["panck"].Fraction);
        Assert.Equal(1,Summary(Quantification.Views([Cell(.9)],T,new Dictionary<Guid,string>())).Ki67!["panck"].Fraction);
        Assert.False(new Thresholds(.35,.35,.35,double.NaN).Valid());
    }
    [Fact] public void V2ComparisonAndExportCarryStatesAndSuppressIncompatibleDifferences()
    {
        var image=new TissueImage {PixelSizeUm=1};
        ResultSnapshot Snapshot(double value) {
            var cells=Quantification.Views([Cell(value)],T,new Dictionary<Guid,string>());
            return new(new AnalysisRun {ModelVersion=Ki67Quantification.Model,AlgorithmVersion=Ki67Quantification.Algorithm},new Roi {Width=100,Height=100},image,cells,Summary(cells),[]);
        }
        var a=Snapshot(.8);var b=Snapshot(.1);
        var result=Comparison.Build(image.Id,a,b);
        Assert.Equal(100,result.Ki67!.DifferencePercentagePoints["panck"]);
        var mismatch=Comparison.Build(image.Id,a,b with {Summary=b.Summary with {Thresholds=new(.35,.35,.35,.6)}});
        Assert.False(mismatch.Ki67!.Comparable);Assert.Null(mismatch.Ki67.DifferencePercentagePoints["panck"]);
        using var zip=new ZipArchive(new MemoryStream(Comparison.Export(result,2)));
        using var reader=new StreamReader(zip.GetEntry("ki67-cells-A.csv")!.Open());
        Assert.Contains("positive,positive,ok",reader.ReadToEnd());
        Assert.NotNull(zip.GetEntry("ki67-summary-B.csv"));
        Assert.Throws<ApiError>(()=>Comparison.Export(result,3));
    }
    [Fact] public void QuantitativeMapMustAgreeWithNuclearValuesAndRejectNonFinitePixels()
    {
        var dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString());Directory.CreateDirectory(dir);
        try
        {
            void Npy(string name,string dtype,Action<BinaryWriter> pixels)
            {
                using var writer=new BinaryWriter(File.Create(Path.Combine(dir,name)));
                writer.Write(new byte[]{0x93,78,85,77,80,89,1,0});
                var header=System.Text.Encoding.ASCII.GetBytes($"{{'descr': '{dtype}', 'fortran_order': False, 'shape': (2, 2), }}\n");
                writer.Write((ushort)header.Length);writer.Write(header);pixels(writer);
            }
            Npy("mask.npy","<i4",w=>{w.Write(1);w.Write(1);w.Write(0);w.Write(0);});
            Npy("map.npy","<f4",w=>{w.Write(.2f);w.Write(.4f);w.Write(.9f);w.Write(.9f);});
            var cell=new MeasuredCell(1,.5,0,2,.8,.8,.1,.1,"ok",[],.3,"ok",2);
            var roi=new Roi{Width=2,Height=2};
            Ki67MapContract.Validate(Path.Combine(dir,"map.npy"),Path.Combine(dir,"mask.npy"),roi,[cell]);
            Assert.Throws<ApiError>(()=>Ki67MapContract.Validate(Path.Combine(dir,"map.npy"),Path.Combine(dir,"mask.npy"),roi,[cell with {Ki67Value=.5}]));
            Npy("map.npy","<f4",w=>{w.Write(.2f);w.Write(.4f);w.Write(float.NaN);w.Write(.9f);});
            Assert.Throws<ApiError>(()=>Ki67MapContract.Validate(Path.Combine(dir,"map.npy"),Path.Combine(dir,"mask.npy"),roi,[cell]));
        }
        finally {Directory.Delete(dir,true);}
    }

}
