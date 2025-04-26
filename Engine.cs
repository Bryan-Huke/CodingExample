using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Engine : Car
{
    [field: SerializeField] public float topSpeed { get; private set; }
    [field: SerializeField] public float acceleration { get; private set; }
    [field: SerializeField] public float deceleration { get; private set; }

    public float currentSpeed { get; private set; }
    float previousSpeed;
    [SerializeField] Throttle throttle;

    bool isWaiting;

    //bool lockOnStop;
    Target targetStop;
    //bool lockOnSlow;
    Target targetSlow;

    /// <summary>
    /// Signals the engine is subscribed to in order to detect changes
    /// </summary>
    private List<Signal> watchingSignals = new List<Signal>();

    protected override void OnEnable()
    {
        base.OnEnable();
        Sim.onSimStart += SimStart;
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        Sim.onSimStart -= SimStart;
    }

    public override void InitializeCar(Train parent, Car previousCar, Car nextCar)
    {
        ResetValues();

        base.InitializeCar(parent, previousCar, nextCar);

        holdsCargo = false;
    }

    private void ResetValues()
    {
        currentSpeed = 0;
        previousSpeed = 0;
        throttle = Throttle.full;

        isWaiting = false;

        if (watchingSignals != null)    // Unsubscribe before clearing list
        {
            foreach (Signal signal in watchingSignals)
            {
                signal.onSignalSwitch -= SignalChange;
            }

            watchingSignals.Clear(); 
        }
        else
        {
            watchingSignals = new List<Signal>();
        }


        targetStop = new Target();
        targetStop.stop = true;
        targetSlow = new Target();
        targetSlow.stop = false;
    }

    private void SimStart()
    {
        Signal signal = targetTrack.GetSignal(currentTrack);
        //throttle = Throttle.full;

        if (signal == null) return;

        // If starting on a signal, latch onto it

        SignalState startingSignalState = signal.GetSignalState(currentTrack);
        if(startingSignalState == SignalState.red)
        {
            watchingSignals.Add(signal);
            signal.onSignalSwitch += SignalChange;

            targetStop.signal = targetTrack.signal;
            targetStop.distance = remainingDistance;
            targetStop.lockedOn = true;
        }
        else if (startingSignalState == SignalState.yellow)
        {
            watchingSignals.Add(signal);
            signal.onSignalSwitch += SignalChange;

            targetSlow.signal = targetTrack.signal;
            targetSlow.distance = remainingDistance;
            targetSlow.lockedOn = true;
        }
    }

    protected override void SimReset()
    {
        parentTrain.SimReset(this);
        base.SimReset();
        ResetValues();
    }

    public void Step(float timeDelta)
    {

        // Search for light and change throttle
        Search();
        AdjustThrottle();

        AdjustSpeed(timeDelta);

        parentTrain.MoveTrain(FindMoveSpeed(timeDelta));

        if (currentSpeed == 0 && previousSpeed > 0)
        {
            targetTrack.TrainStopped(parentTrain, currentTrack);
        }
    }

    /// <summary>
    /// Searches along the track it's heading down to look for an signals
    /// Uses (a little more than) its stopping distance and a distance to search since signals past that are irrelavent 
    /// </summary>
    private void Search()
    {
        if (isWaiting) return;

        if (currentSpeed == 0 && targetStop.signal != null && targetStop.signal.state == SignalState.red)
            return;

        float searchDepth = GetStoppingDistance(currentSpeed, 0);
        float distanceToPrev = currentTrack.DistanceToTrack(targetTrack) - remainingDistance;
        SearchLoop(searchDepth + distanceToPrev, -distanceToPrev, targetTrack, currentTrack);
    }


    // If the game ends up needing optimization, START WITH THIS FUNCTION
    /// <summary>
    /// Recursive searching loop. Checks a track, then moves on to the next track in a line.
    /// Will add any signals to its watch list.
    /// (Closest) Red and yellow signals are marked so that the engine and slow or stop for them
    /// </summary>
    /// <param name="remainingDistance"></param>
    /// <param name="searchDistance"></param>
    /// <param name="track"></param>
    /// <param name="prev"></param>
    private void SearchLoop(float remainingDistance, float searchDistance, TrackConnections track, TrackConnections prev)
    {
        float trackDistance = prev.DistanceToTrack(track);
        searchDistance += trackDistance;

        Signal signal = track.signal;

        //  If search finds the signal we are already stopping for, end search
        if (signal != null && signal == targetStop.signal && signal.GetSignalState(prev) == SignalState.red)
        {
            targetStop.distance = searchDistance;
            return;
        }

        // If search finds a signal the engine wasnt already listening to, check it out more 
        if (signal != null && signal.GetSignalState(prev) != SignalState.none && !watchingSignals.Contains(signal))
        {
            // Listen to found signal
            watchingSignals.Add(signal);
            signal.onSignalSwitch += SignalChange;

            switch (signal.GetSignalState(prev))
            {
                case SignalState.red:

                    if (searchDistance <= 0.15f)
                    {
                        Debug.LogWarning("RUNNING IT"); // Found a red signal that right in front of the train. Ignoring it prevents the weird edge case of a signal turning red right as the train is passing it, then slowing down for seemingly no reason
                        break;
                    }

                    targetStop.signal = signal;
                    targetStop.lockedOn = GetStoppingDistance(currentSpeed, 0) <= searchDistance;   // Lock on if the train can stop in time
                    targetStop.distance = searchDistance;

                    return;

                case SignalState.yellow:
                    // Ignore if engine is already slowing for this signal
                    if (targetSlow.signal != null && ((signal == targetSlow.signal) || targetSlow.distance < searchDistance))
                        break;

                    targetSlow.signal = signal;
                    targetSlow.lockedOn = GetStoppingDistance(currentSpeed, topSpeed / 2) <= searchDistance;    // Lock on if the train can slow down in time
                    targetSlow.distance = searchDistance;

                    break;

                default:
                    break;
            }
        }

        remainingDistance -= trackDistance;

        // Break if search has gone target distance
        if (remainingDistance < 0)
        {
            return;
        }

        // Search again on the next track
        SearchLoop(remainingDistance, searchDistance, track.GetNextTrack(prev), track);
    }


    private void AdjustThrottle()
    {
        // Throttle logic here should only be slowing the train. Everything that increases the throttle should be triggered elsewhere


        if (throttle == Throttle.stop)
            return;

        if (throttle == Throttle.wait)
        {
            // Check for dispatch (This should probably be handled elsewhere)

            return;
        }

        // Throttle must be full or slow by here

        // If train is moving and target stop signal is close enough to begin braking
        if (CheckTarget(targetStop) && targetStop.distance <= GetStoppingDistance(currentSpeed, 0))
        {
            throttle = Throttle.stop;
            return;
        }

        if (throttle == Throttle.full && CheckTarget(targetSlow) && targetSlow.distance <= GetStoppingDistance(currentSpeed, topSpeed / 2))
        {
            throttle = Throttle.slow;
        }
    }

    /// <summary>
    /// Changes engine speed based on current speed and throttle.
    /// Aka handles high level acceleration and deceleration
    /// </summary>
    /// <param name="timeDelta"></param>
    private void AdjustSpeed(float timeDelta)
    {
        previousSpeed = currentSpeed;

        switch (throttle)
        {
            case Throttle.full:
                Accelerate(topSpeed, timeDelta);
                break;
            case Throttle.slow:
                if (currentSpeed <= topSpeed / 2)
                    Accelerate(topSpeed / 2, timeDelta);
                else
                    Deccelerate(topSpeed / 2, timeDelta);
                break;
            case Throttle.stop:
                Deccelerate(0, timeDelta);
                break;
            default:
                break;
        }
    }

    private float GetStoppingDistance(float trainSpeed, float targetSpeed)
    {
        if (targetSpeed > trainSpeed)
        {
            return 0;
            //Debug.LogError($"Trying to find stopping distance at invalid speeds");
        }

        return ((trainSpeed + targetSpeed) / 2) * ((trainSpeed - targetSpeed) / deceleration);
    }

    private float FindMoveSpeed(float timeDelta)
    {
        float moveDist = Mathf.Max((currentSpeed + previousSpeed) / 2, 0f) * timeDelta;
        if (!targetStop.lockedOn) return moveDist;

        if (targetStop.distance <= moveDist)
        {
            moveDist = Mathf.Max(targetStop.distance - 0.001f, 0);
            currentSpeed = 0;
        }

        return moveDist;

    }

    private void Accelerate(float targetSpeed, float timeDelta)
    {
        if (previousSpeed == targetSpeed)
            return;

        currentSpeed = Mathf.Min(currentSpeed + (timeDelta * acceleration), targetSpeed);
    }

    private void Deccelerate(float targetSpeed, float timeDelta)
    {
        currentSpeed = Mathf.Max(currentSpeed - (timeDelta * deceleration), targetSpeed);
    }

    public override void MoveCar(float moveDistance)
    {
        base.MoveCar(moveDistance);

        if (CheckTarget(targetSlow))
            targetSlow.distance -= moveDistance;

        if (CheckTarget(targetStop))
            targetStop.distance -= moveDistance;

        SimTracking.AddDistance(moveDistance);  // Track train's movement for stat tracking purposes

    }

    protected override TrackConnections SwitchTracks()
    {
        TrackConnections previousTrack = base.SwitchTracks();

        Signal signal = currentTrack.EnterTrack(parentTrain, previousTrack);

        bool skipClear = false;

        // If track change does not have a signal, we're done
        if (signal == null || signal.GetSignalState(previousTrack) == SignalState.none)
            return previousTrack;

        // If signal is green, set throttle back to full
        if (throttle == Throttle.slow && signal.state == SignalState.green)
            throttle = Throttle.full;

        // If passing our target yellow signal, we can forget it now
        if (signal == targetSlow.signal)
        {
            ClearSlowTarget();
        }

        // If going past our intended stop..
        if (signal == targetStop.signal && throttle == Throttle.stop)
        {
            if (!targetStop.lockedOn)
            {
                // Train was not locked onto the stop so it will run the red and continue in slow mode
                throttle = Throttle.slow;
                ClearStopTarget();
            }
            else
            {
                // Train was locked on but didnt stop. THis should not happen but does sometimes?? Fix me
                Debug.LogWarning("Somehow missed locked on stop???");
                skipClear = true;
            }
        }

        // Stop listening to the signal we are passing
        if (watchingSignals.Contains(signal) && !skipClear)
        {
            signal.onSignalSwitch -= SignalChange;
            watchingSignals.Remove(signal);
        }

        return previousTrack;
    }

    /// <summary>
    /// Will get called when a signal within our stopping distance changes. Aka react to it right away
    /// </summary>
    /// <param name="signal"></param>
    /// <param name="signalState"></param>
    private void SignalChange(Signal signal, SignalState signalState)
    {
        // If we were going to (or did) stop at this signal
        if (signal == targetStop.signal)
        {

            if (isWaiting)
            {
                if (signalState == SignalState.red)
                {
                    isWaiting = false;
                    return;
                }

                if (signalState == SignalState.green)
                {
                    isWaiting = false;
                    ClearStopTarget();
                    throttle = Throttle.full;
                }
            }
            else if (signalState != SignalState.red)
            {
                if (parentTrain.isActiveLoading && signalState == SignalState.yellow)
                {
                    // Wait for load to end
                    isWaiting = true;
                    return;
                }

                ClearStopTarget();
                throttle = signalState == SignalState.green ? Throttle.full : Throttle.slow;
            }
        }

        // If we were going to slow at this signal, just forget it and let the next search handle it
        if (signal == targetSlow.signal && signalState != SignalState.yellow)
        {
            ClearSlowTarget();
            throttle = Throttle.full;
        }

        // Disconnect signal from watch list so that it can be readded in it's new state during the next search
        signal.onSignalSwitch -= SignalChange;
        watchingSignals.Remove(signal);

        // !!!Add a search call here if search is changed from happening every step!!!
    }

    public void EndLoading()
    {
        Debug.Log("Finished loading");

        if (!isWaiting) return;

        // If train was waiting at a yellow signal while loading, clear the stop and proceed full throttle
        isWaiting = false;

        targetStop.signal.onSignalSwitch -= SignalChange;
        watchingSignals.Remove(targetStop.signal);
        ClearStopTarget();
        throttle = Throttle.full;
    }

    private bool CheckTarget(Target target)
    {
        return target.signal != null && target.signal.state != SignalState.none;
    }

    private void ClearStopTarget()
    {
        targetStop.signal = null;
        targetStop.distance = -1;
        targetStop.lockedOn = false;
    }


    private void ClearSlowTarget()
    {
        targetSlow.signal = null;
        targetSlow.distance = -1;
        targetSlow.lockedOn = false;
    }

}

enum Throttle
{
    stop,
    slow,
    full,
    wait
}