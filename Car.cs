using UnityEngine;

public class Car : MonoBehaviour
{
    // Track data
    [field: SerializeField] public TrackConnections currentTrack {get; protected set;}
    [field: SerializeField] public TrackConnections targetTrack { get; protected set; }

    // Movement data
    [SerializeField] private Vector2 direction;
    [SerializeField] protected float remainingDistance;

    // Train data
    [field: SerializeField] public Train parentTrain { get; private set; }
    [field: SerializeField] public Car previousCar { get; private set; }
    [field: SerializeField] public Car nextCar { get; private set; }

    // Cargo data
    [field: SerializeField] public bool holdsCargo { get; protected set; }
    [field: SerializeField] public LoadType loadType { get; set; }
    [field: SerializeField] public float cargoCount { get; private set; }



    [SerializeField] CarAnimator animator;


    /// <summary>
    /// Distance past current track. Only used for Limbo
    /// </summary>
    [field: SerializeField] float pastDistance = -1000; 
    [field: SerializeField] private bool inLimbo;
    [field: SerializeField] private bool startInLimbo;

    // Starting data
    [SerializeField] private TrackConnections startingTrack, startingTarget;
    [SerializeField] private Vector3 startingPos;
    [field: SerializeField] private float startRemainDistance;
    [field: SerializeField] private Quaternion startingRotation;

    private void Awake()
    {
        currentTrack.CarEnter(this);
    }

    protected virtual void OnEnable()
    {
        Sim.onSimStart += SimStart;
        Sim.onSimResetLate += SimReset;
    }

    protected virtual void OnDisable()
    {
        Sim.onSimStart -= SimStart;
        Sim.onSimResetLate -= SimReset;
    }

    public void SetTrack(TrackConnections start, TrackConnections target)
    {
        currentTrack = start;
        targetTrack = target;
    }

    public virtual void InitializeCar(Train parent, Car previousCar, Car nextCar)
    {
        parentTrain = parent;
        this.previousCar = previousCar;
        this.nextCar = nextCar;
        holdsCargo = true;

        //FindDirectionToNextTrack();  
        if (!inLimbo) 
            RotateTowardsTarget();

        currentTrack.CarEnter(this);

        animator?.UpdateSprite(cargoCount, loadType);
    }

    public virtual void MoveCar(float moveDistance)
    {
        // If the car is in Limbo, break here and let Limbo handle movement
        if (inLimbo)
        {
            if (RunLimbo(ref moveDistance)) return;
        }
        else if (moveDistance + (StaticTrainSpawner.carSeperation / 2f) >= remainingDistance)   // Enter limbo check
        {
            inLimbo = true;
            if (RunLimbo(ref moveDistance)) return;
        }

        /*    Obsolete cause of new Limbo thingy
        if (moveDistance >= remainingDistance)
        {
            moveDistance -= remainingDistance;
            transform.position = targetTrack.transform.position;
            SwitchTracks();
        }*/

        Vector2 pos = transform.position;
        pos += direction * moveDistance;
        transform.position = pos;
        remainingDistance -= moveDistance;

        RotateTowardsTarget();
    }

    /// <summary>
    /// Limbo is the brief moment where the car detaches from following the track exactly.
    /// This should be called every frame when the train car would normally overlap the track change.
    /// Limbo is meant to make the cars change tracks smoothly instead of snapping, only adjust this if there are movement issues right when switching tracks
    /// </summary>
    /// <param name="moveDistance"></param>
    /// <returns>Returns true if car remains in limbo</returns>
    private bool RunLimbo(ref float moveDistance)
    {
        // Break out of limbo case (moved too far past track change)
        if (pastDistance + moveDistance >= (StaticTrainSpawner.carSeperation / 2f))
        {
            // Snap back to normal movement
            moveDistance -= (StaticTrainSpawner.carSeperation / 2f) - pastDistance;
            remainingDistance = currentTrack.DistanceToTrack(targetTrack) - (StaticTrainSpawner.carSeperation / 2f);

            Vector3 offset = -direction * remainingDistance;
            transform.position = targetTrack.transform.position + offset;


            // Detach from limbo
            inLimbo = false;
            pastDistance = -1000;
            return false;
        }

        Vector3 frontTarget, backTarget;    // Points the car will try to position between


        /*
         * Find normalized direction from point of track change to other track
         * Mult the direction by distance of where the end of the car would be if traveling along the track
         * Set target to track change point plus offset
         */

        if (pastDistance < 0)   // Before track change
        {
            Vector3 backOffset = -direction * (remainingDistance - moveDistance + (StaticTrainSpawner.carSeperation / 2f));
            backTarget = targetTrack.transform.position + backOffset;

            Vector3 frontOffset = (targetTrack.GetNextTrack(currentTrack).transform.position - targetTrack.transform.position).normalized;
            frontOffset *= (StaticTrainSpawner.carSeperation / 2f) - remainingDistance + moveDistance;
            frontTarget = targetTrack.transform.position + frontOffset;
        }
        else    // After track change
        {
            Vector3 backOffset = (currentTrack.GetNextTrack(targetTrack).transform.position - currentTrack.transform.position).normalized; 
            backOffset *= ((StaticTrainSpawner.carSeperation / 2f) - pastDistance) - moveDistance;
            backTarget = currentTrack.transform.position + backOffset;

            Vector3 frontOffset = direction * (pastDistance + moveDistance + (StaticTrainSpawner.carSeperation / 2f));
            frontTarget = currentTrack.transform.position + frontOffset;
        }

        if (moveDistance >= remainingDistance)  // If moving past track change
        {
            moveDistance -= remainingDistance;
            SwitchTracks();
            pastDistance = 0;
        }

        //Vector3 offset = -direction * (remainingDistance - moveDistance);
        //backTarget = targetTrack.transform.position + offset;

        pastDistance += moveDistance;
        remainingDistance -= moveDistance;

        // Position between target points and rotate accordingly
        transform.position = (frontTarget + backTarget) / 2f;
        RotateTowardsTarget((frontTarget - transform.position).normalized);

        return true;
    }

    /// <summary>
    /// Tries to snap the car into limbo.
    /// Use this when setting up or reseting the car, NOT DURING THE SIMULATION
    /// </summary>
    public void TryRunLimbo()
    {
        FindDirectionToNextTrack();

        float val = (StaticTrainSpawner.carSeperation / 2f);
        if (remainingDistance < val || currentTrack.DistanceToTrack(targetTrack) - remainingDistance < val)
        {
            if (currentTrack.DistanceToTrack(targetTrack) - remainingDistance < val)
            {
                pastDistance = currentTrack.DistanceToTrack(targetTrack) - remainingDistance;
            }

            float temp = -0.0001f;  // Move car microscopically backwards so that it will respect a signal it starts on (Shouldnt affect stat tracking since the function shouldnt be run during the simulation)
            inLimbo = true;
            startingPos = transform.position;
            RunLimbo(ref temp);
        }
    }

    /// <summary>
    /// Rotates car to face towards the next track
    /// </summary>
    public void RotateTowardsTarget()
    {
        RotateTowardsTarget(direction);
    }

    private void RotateTowardsTarget(Vector2 targetDirection)
    {
        if (targetDirection != Vector2.zero)
        {
            float angle = Mathf.Atan2(targetDirection.y, targetDirection.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.AngleAxis(angle, Vector3.forward);
        }
        else
            Debug.LogWarning("Train car is trying to rotate to an unset direction, this should happen..  Go fix it");
    }

    /// <summary>
    /// Call this when the car reaches it's target track. Updates all relavent data 
    /// </summary>
    protected virtual TrackConnections SwitchTracks()
    {
        TrackConnections previousTrack = currentTrack;
        currentTrack = targetTrack;
        targetTrack = currentTrack.GetNextTrack(previousTrack);

        previousTrack.CarExit(this);
        currentTrack.CarEnter(this);

        FindDirectionToNextTrack();

        return previousTrack;
    }

    private void FindDirectionToNextTrack()
    {
        direction = (targetTrack.transform.position - currentTrack.transform.position).normalized;
        remainingDistance = targetTrack.DistanceToPosition(transform.position);
    }

    /// <summary>
    /// Tries to load or unload cargo into the car. 
    /// Since cars have sections that get loaded one at a time, this will only try to load one section so that the loader can move onto the next car if a section is filled.
    /// </summary>
    /// <param name="amount">Amount to load. This value is altered so that overfilling can be added to the next car</param>
    /// <param name="load">True for loading, false for unloading</param>
    /// <returns>Returns true if any cargo was loaded/unloaded</returns>
    public virtual bool LoadCar(ref float amount, bool load)
    {
        // Break out if trying to load a full car or unload an empty car
        if ((load && cargoCount == 100) || !load && cargoCount == 0)
            return false;

        ///Loading vs Unloading modifier 
        ///Interval stuff is only additive so only use this when actually changing the car's carago
        float mod = load ? 1 : -1;  

        float intervalSize = animator.GetInterval(loadType);    
        float nextInterval = load ? intervalSize - (cargoCount % intervalSize) : cargoCount % intervalSize; // Find amount/distance to next interval
        if (nextInterval == 0)  // Checks if cargo is exactly on an interval breakpoint. Load/unload anyway cause stopping on a breakpoint would move onto the next car anyway
            nextInterval = intervalSize;

        float extra = -1;   // Init as negative so we know the car isnt perfectly filled. Edge case of loading exactly to the interval (and returning amount as 0) should still move the loader to the next car

        float loadedAmount; // Track for updating objectives

        // Adjust cargo value
        if (amount > nextInterval)
        {
            extra = amount - nextInterval;
            cargoCount = Mathf.Clamp(cargoCount + (nextInterval * mod), 0, 100);
            loadedAmount = nextInterval;
        }
        else
        {
            cargoCount = Mathf.Clamp(cargoCount + (amount * mod), 0, 100);
            loadedAmount = amount;

            if (amount == nextInterval) // Edge case of perfect load, move loader to next car
                extra = 0;
        }

        // If unloading cargo, send data to game logic
        if (!load)
        {
            SimTracking.AddDelivery(loadType, loadedAmount);
        }

        //Visuals
        animator.UpdateSprite(cargoCount, loadType);

        //Debug.Log($"Car loaded to {cargoCount}");

        amount = extra;

        return true;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="loading">True if trying to load, false if trying to unload</param>
    /// <returns>True if loading and full, or if unloading and empty</returns>
    public bool CheckFilled(bool loading)
    {
        if (loading)
        {
            return cargoCount == 100;
        }

        return cargoCount == 0;
    }

    private void SimStart()
    {
        // Store starting values so that car can be easily reset to its starting state
        if(!inLimbo)
            startingPos = transform.position;
        startingTrack = currentTrack;
        startingTarget = targetTrack;

        /*
        startInLimbo = inLimbo;
        startPastDistance = pastDistance;
        startingRotation = transform.rotation;
        */
    }

    protected virtual void SimReset()
    {
        currentTrack.CarExit(this);

        cargoCount = 0;

        // Add Cargo visuals here
        animator?.UpdateSprite(cargoCount, loadType);

        SetTrack(startingTrack, startingTarget);

        inLimbo = false;
        pastDistance = -1000;

        transform.position = startingPos;

        FindDirectionToNextTrack();
        //remainingDistance = startRemainDistance;

        TryRunLimbo();
        if (!inLimbo)
            RotateTowardsTarget();

        currentTrack.CarEnter(this);
        //transform.rotation = startingRotation;

    }

}
