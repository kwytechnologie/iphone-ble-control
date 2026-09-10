using Xunit;

namespace BleHid.Core.Tests;

public sealed class RemoteReturnTests
{
    [Theory]
    [InlineData(ScreenEdge.Right,-350,0)]
    [InlineData(ScreenEdge.Left,350,0)]
    [InlineData(ScreenEdge.Top,0,350)]
    [InlineData(ScreenEdge.Bottom,0,-350)]
    public void Opposite_edge_returns_for_each_phone_position(ScreenEdge side,int dx,int dy)
    {
        var estimate = new RemoteEdgeEstimate(side,600);
        estimate.Reset(0);
        Assert.True(estimate.Move(dx,dy,false,800));
    }

    [Fact]
    public void Entry_guard_prevents_immediate_bounce()
    {
        var estimate = new RemoteEdgeEstimate(ScreenEdge.Right,600);
        estimate.Reset(0);
        Assert.False(estimate.Move(-1000,0,false,100));
        Assert.True(estimate.Move(-50,0,false,900));
    }

    [Fact]
    public void Held_inputs_prevent_return_and_do_not_accumulate_pressure()
    {
        var estimate = new RemoteEdgeEstimate(ScreenEdge.Right,600);
        estimate.Reset(0);
        Assert.False(estimate.Move(-1000,0,true,900));
        Assert.False(estimate.Move(-20,0,false,1000));
        Assert.True(estimate.Move(-20,0,false,1100));
    }

    [Fact]
    public void Increasing_travel_delays_estimated_boundary()
    {
        var estimate = new RemoteEdgeEstimate(ScreenEdge.Right,1200);
        estimate.Reset(0);
        Assert.False(estimate.Move(-350,0,false,900));
        Assert.True(estimate.Move(-300,0,false,1000));
    }

    [Fact]
    public void Opposite_direction_and_perpendicular_motion_do_not_return()
    {
        var estimate = new RemoteEdgeEstimate(ScreenEdge.Right,600);
        estimate.Reset(0);
        Assert.False(estimate.Move(10000,0,false,900));
        Assert.False(estimate.Move(0,-10000,false,1000));
        Assert.False(estimate.Move(-600,0,false,1100));
        Assert.True(estimate.Move(-40,0,false,1200));
    }

    [Fact]
    public void Middle_release_is_consumed_after_immediate_local_recovery()
    {
        var gesture = new MiddleReturnGesture();
        Assert.Equal(ReturnGesture.ReturnLocal,gesture.Handle(0x0207,true,true));
        Assert.Equal(ReturnGesture.Consume,gesture.Handle(0x0208,false,true));
        Assert.Equal(ReturnGesture.None,gesture.Handle(0x0208,false,true));
    }

    [Theory]
    [InlineData(0x020A,true,true)]
    [InlineData(0x0207,false,true)]
    [InlineData(0x0207,true,false)]
    [InlineData(0x0201,true,true)]
    [InlineData(0x0204,true,true)]
    public void Scroll_and_normal_clicks_remain_unchanged(int message,bool remote,bool enabled)
    {
        Assert.Equal(ReturnGesture.None,new MiddleReturnGesture().Handle(message,remote,enabled));
    }
}
