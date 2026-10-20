using System.Collections.Concurrent;

namespace Verdant.App;

// No HID, hooks, windows, or generated inputs. Runs the production coordinator
// against a recording backend to exercise lifecycle races deterministically.
internal static class HardwareCoordinatorTests
{
    internal static async Task Run(string report)
    {
        var results=new List<string>();
        var devices=new ConcurrentQueue<Fake>();
        var releases=new ConcurrentQueue<bool>();
        var errors=new ConcurrentQueue<string>();
        var binding=new Binding { Kind=ActionKind.Mouse,Mouse=ButtonId.Forward,Mode=PlayMode.Toggle,
            ExtraOnDevice=true,ExtraKeyEnabled=true,ExtraKeys=[70] };
        var state=new OnMouseExtra.State(binding,true,1,0,false);
        OnMouseExtra.State Read()=>Volatile.Read(ref state);
        void Set(OnMouseExtra.State value)=>Volatile.Write(ref state,value);
        async Task Wait(Func<bool> condition)
        {
            var end=Environment.TickCount64+3000;
            while(!condition() && Environment.TickCount64<end) await Task.Delay(5);
            if(!condition()) throw new Exception("Coordinator condition timed out.");
        }
        try
        {
            using var coordinator=new OnMouseExtra(Read,s=>{
                errors.Enqueue(s); Set(Read() with { CanArm=false });
            },_=>{ var d=new Fake(); devices.Enqueue(d); return d; },(_,mouse)=>releases.Enqueue(mouse));
            await Wait(()=>coordinator.Ready);
            var first=devices.Single();
            Set(Read() with { Activation=1,Active=true });
            await Wait(()=>first.Detaches==1 && !coordinator.Ready);
            if(first.Cancels!=0) throw new Exception("Detached trigger cancelled an active hold.");
            results.Add("PASS start detaches only the trigger while preserving active playback.");
            Set(Read() with { Active=false });
            await Wait(()=>first.Cancels==1 && first.Arms==2 && coordinator.Ready);
            if(!releases.Contains(true)) throw new Exception("OFF did not release outputs.");
            results.Add("PASS OFF cancels hardware before rearming and releases outputs.");
            Set(Read() with { Activation=2,Active=false }); // ON and OFF between polls.
            await Wait(()=>first.Cancels==2 && first.Arms==3);
            results.Add("PASS a brief ON/OFF between polls still cancels the hardware.");
            var releaseCount=releases.Count;
            Set(Read() with { CanArm=false,Generation=2 });
            await Wait(()=>first.Disposals==1 && coordinator.Stopped);
            var count=devices.Count; await Task.Delay(50);
            if(devices.Count!=count || coordinator.Ready) throw new Exception("Paused controller rearmed.");
            results.Add("PASS pause/editor focus disposes playback and keeps the mapping unarmed.");
            if(releases.Count!=releaseCount) throw new Exception("Idle cleanup injected releases despite owning no input.");
            results.Add("PASS idle desktop loss restores hardware without injecting unnecessary releases.");
            Set(Read() with { CanArm=true,Active=true,Activation=3 });
            await Task.Delay(50);
            if(devices.Count!=count) throw new Exception("Controller changed mapping during an existing normal hold.");
            Set(Read() with { Active=false });
            await Wait(()=>devices.Count==2 && coordinator.Ready);
            var second=devices.Last();
            results.Add("PASS preparation waits for an existing normal mouse hold to end.");
            var replacement=binding with { ExtraIntervalMs=1500 };
            Set(Read() with { Binding=replacement,Generation=3 });
            await Wait(()=>second.Disposals==1 && devices.Count==3 && coordinator.Ready);
            results.Add("PASS profile/configuration replacement disposes the old device session.");
            var third=devices.Last();
            third.FailNextArm=true;
            Set(Read() with { Activation=4,Active=true });
            await Wait(()=>third.Detaches==1);
            Set(Read() with { Active=false });
            await Wait(()=>!errors.IsEmpty && third.Disposals==1);
            var failedCount=devices.Count; await Task.Delay(50);
            if(devices.Count!=failedCount || coordinator.Ready) throw new Exception("Failed hardware kept rearming.");
            results.Add("PASS hardware failure cleans up, reports the error, and stops rearming.");
            Set(Read() with { CanArm=true,Generation=4 });
            await Wait(()=>devices.Count==4 && coordinator.Ready);
            results.Add("PASS explicit resume can create a fresh hardware session.");
            var fourth=devices.Last();
            releaseCount=releases.Count;
            Set(Read() with { Activation=5,Active=false,CanArm=false,Generation=5 });
            await Wait(()=>fourth.Disposals==1 && coordinator.Stopped);
            if(releases.Count!=releaseCount+1) throw new Exception("Unobserved activation was lost during desktop cleanup.");
            results.Add("PASS activation immediately before desktop loss still releases its owned inputs.");
        }
        catch(Exception ex) { results.Add("FAIL "+ex.Message); }
        results.Add(results.Any(s=>s.StartsWith("FAIL"))?"FAILED":"ALL COORDINATOR CHECKS PASSED");
        File.WriteAllLines(report,results);
    }
    private sealed class Fake : IOnMousePlayback
    {
        public bool Armed { get; private set; }
        public volatile bool FailNextArm;
        public int Arms,Detaches,Cancels,Disposals;
        public void Arm() { if(FailNextArm) throw new IOException("Simulated HID failure."); Armed=true; Interlocked.Increment(ref Arms); }
        public void Stop() { Armed=false; Interlocked.Increment(ref Detaches); }
        public void CancelPlayback() { Interlocked.Increment(ref Cancels); }
        public void Dispose() { Armed=false; Interlocked.Increment(ref Disposals); }
    }
}
