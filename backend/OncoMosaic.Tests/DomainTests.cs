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
        Assert.False(new Rectangle(0,0,0,2).Valid(256,256));
        Assert.False(new Rectangle(int.MaxValue,0,10,2).Valid(256,256));
    }
    [Fact] public void RetryPreservesIdentityAndCountsAttempts()
    {
        var r = new AnalysisRun(); var id = r.Id; r.Start();
        Assert.Equal(1,r.Attempt); Assert.Throws<InvalidOperationException>(()=>r.Start());
        Assert.Throws<ApiError>(()=>r.Retry()); r.Status="Failed"; r.Retry(); r.Start();
        Assert.Equal(id,r.Id); Assert.Equal(2,r.Attempt); Assert.Equal("Running",r.Status);
    }
    [Fact] public void StatisticsUsePhysicalUnitsAndReviewsPreserveAutomaticLabels()
    {
        var a = new Cell{X=10,Y=20,PanckValue=.8,Cd8Value=.1};
        var b = new Cell{X=13,Y=24,PanckValue=.1,Cd8Value=.9};
        var roi = new Roi{Width=100,Height=100}; var t=new Thresholds(.35,.35);
        var original=Quantification.Views([a,b],t,new Dictionary<Guid,string>());
        var summary=Quantification.Calculate(original,roi,2,t,0);
        Assert.Equal(.04,summary.AreaMm2,10); Assert.Equal(.5,summary.PanckFraction);
        Assert.Equal(25,summary.PanckDensity); Assert.Equal(10,summary.MeanNearestDistanceUm);
        var reviewed=Quantification.Views([a,b],t,new Dictionary<Guid,string>{{a.Id,"excluded"}});
        var next=Quantification.Calculate(reviewed,roi,2,t,1);
        Assert.Equal("panck",reviewed[0].AutoLabels); Assert.Equal("excluded",reviewed[0].EffectiveLabels);
        Assert.Equal(1,next.Counts.Valid); Assert.Null(next.MeanNearestDistanceUm); Assert.Equal(1,next.Cd8Fraction);
        Assert.Equal(.8,a.PanckValue);
    }
    [Fact] public void EmptyAndOutOfRoiObjectsHaveNoMisleadingZeroDistance()
    {
        var c=new Cell{X=100,Y=100,PanckValue=.8,Cd8Value=.9};var t=new Thresholds(.35,.35);
        var result=Quantification.Calculate(Quantification.Views([c],t,new Dictionary<Guid,string>()),new Roi{Width=50,Height=50},.5,t,0);
        Assert.Equal(0,result.Counts.Total);Assert.Null(result.PanckFraction);Assert.Null(result.Cd8Fraction);Assert.Null(result.MeanNearestDistanceUm);
    }
    [Fact] public void ThresholdsCreateDifferentClassificationsWithoutMutatingMeasurements()
    {
        var c=new Cell{PanckValue=.6,Cd8Value=.4};
        Assert.Equal("double-positive",Quantification.Auto(c,new(.35,.35)));
        Assert.Equal("panck",Quantification.Auto(c,new(.5,.5)));
        Assert.Equal("negative",Quantification.Auto(c,new(.8,.8)));
        Assert.False(new Thresholds(double.NaN,.5).Valid());
    }
}
