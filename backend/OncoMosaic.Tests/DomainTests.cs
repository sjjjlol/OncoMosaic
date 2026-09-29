using OncoMosaic;
using Xunit;
namespace OncoMosaic.Tests;
public class DomainTests
{
    [Fact] public void RoiBoundsAndOriginalCoordinates()
    {
        var r = new Rectangle(20,30,180,160);
        Assert.True(r.Valid(256,256)); Assert.True(r.Contains(199.9,189.9));
        Assert.False(r.Contains(200,190)); Assert.False(new Rectangle(250,0,10,2).Valid(256,256));
    }
    [Fact] public void RetryPreservesIdentityAndCountsAttempts()
    {
        var r = new AnalysisRun(); var id = r.Id; r.Start();
        Assert.Equal(1,r.Attempt); Assert.Throws<InvalidOperationException>(()=>r.Start());
        Assert.Throws<ApiError>(()=>r.Retry()); r.Status="Failed"; r.Retry(); r.Start();
        Assert.Equal(id,r.Id); Assert.Equal(2,r.Attempt);
    }
    [Fact] public void TissueAreaAndDistinctCellDistanceUsePhysicalUnits()
    {
        var a = new Cell{X=10,Y=20,PanckValue=.8,Cd3Value=.1,Cd8Value=.1};
        var b = new Cell{X=13,Y=24,PanckValue=.1,Cd3Value=.9,Cd8Value=.9};
        var roi = new Roi{Width=100,Height=100}; var t=new Thresholds(.35,.35,.35);
        var original=Quantification.Views([a,b],t,new Dictionary<Guid,string>());
        var summary=Quantification.Calculate(original,roi,2,5000,t,0);
        Assert.Equal(.02,summary.AreaMm2,10); Assert.Equal(.5,summary.PanckFraction);
        Assert.Equal(50,summary.PanckDensity); Assert.Equal(10,summary.MeanNearestDistanceUm);
        var reviewed=Quantification.Views([a,b],t,new Dictionary<Guid,string>{{a.Id,"excluded"}});
        var next=Quantification.Calculate(reviewed,roi,2,5000,t,1);
        Assert.Equal("panck",reviewed[0].AutoLabels); Assert.Equal("excluded",reviewed[0].EffectiveLabels);
        Assert.Equal(1,next.Counts.Valid); Assert.Null(next.MeanNearestDistanceUm);
    }
    [Fact] public void AmbiguousAndCropEdgeObjectsAreNotCountedAsKnownPhenotypes()
    {
        var both=new Cell{X=10,Y=10,PanckValue=.8,Cd3Value=.9,Cd8Value=.9};
        var edge=new Cell{X=20,Y=20,PanckValue=.8,QualityFlag="crop-edge"};
        var t=new Thresholds(.35,.35,.35);
        Assert.Equal("unclassified",Quantification.Auto(both,t));
        Assert.Equal("unclassified",Quantification.Auto(edge,t));
        var summary=Quantification.Calculate(Quantification.Views([both,edge],t,new Dictionary<Guid,string>()),new Roi{Width=50,Height=50},.5,1500,t,0);
        Assert.Equal(2,summary.Counts.Unclassified); Assert.Equal(0,summary.Counts.Valid);
        Assert.Null(summary.PanckFraction); Assert.Null(summary.MeanNearestDistanceUm);
    }
    [Fact] public void ThresholdsAreValidated()
    {
        Assert.False(new Thresholds(double.NaN,.5,.5).Valid());
        Assert.False(new Thresholds(.5,-1,.5).Valid());
        Assert.True(new Thresholds(.35,.35,.35).Valid());
        Assert.Throws<System.Text.Json.JsonException>(() => Json.Read<Thresholds>("{\"panck\":0.35,\"cd8\":0.35}"));
    }
}
