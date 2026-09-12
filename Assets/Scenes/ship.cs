using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PlugAndPlay_CruiserController : MonoBehaviour
{
    [Header("=== CRUISER PERFORMANCE ===")]
    [Tooltip("Top speed. Cruisers are fast. 35-45 is the sweet spot.")]
    public float maxSpeed = 40f;
    [Tooltip("Reverse speed (much slower than forward).")]
    public float maxReverseSpeed = 12f;
    public float acceleration = 8f;
    public float deceleration = 10f;

    [Header("=== STEERING & DRIFT ===")]
    [Tooltip("How sharp the ship turns.")]
    public float maxTurnRate = 18f;
    [Tooltip("How fast the rudder physically responds to your input.")]
    public float rudderResponsiveness = 2.5f;
    [Tooltip("How much the back end kicks out when turning hard (0 = train tracks, 0.3 = heavy cruiser drift).")]
    [Range(0f, 0.5f)] public float driftFactor = 0.25f;
    [Tooltip("At top speed, the turning circle widens.")]
    [Range(0f, 1f)] public float highSpeedTurnPenalty = 0.3f;

    [Header("=== VISUAL TILT (Heeling/Pitching) ===")]
    [Tooltip("Leave empty to auto-find your ship's 3D mesh!")]
    public Transform visualMeshOverride;
    [Tooltip("How much the ship leans into turns.")]
    public float heelMultiplier = 3.5f;
    [Tooltip("How much the ship dips forward when accelerating.")]
    public float pitchMultiplier = 2f;

    [Header("=== CINEMATIC CAMERA ===")]
    [Tooltip("Leave empty to auto-calculate the center of your ship!")]
    public Transform cameraPivotOverride;
    public float defaultDistance = 45f;
    public float tacticalDistance = 110f;
    [Tooltip("Camera won't get closer than this (prevents clipping).")]
    public float minDistance = 15f;
    
    [Header("Camera 'Juice' & Dynamics")]
    public float cameraRotationalLag = 0.15f;
    public float baseFOV = 60f;
    public float maxSpeedFOV = 72f;
    public float cameraOrbitSpeed = 3f;
    public float cameraZoomSpeed = 15f;
    [Range(5f, 80f)] public float minPitch = 10f;
    [Range(5f, 80f)] public float maxPitch = 65f;

    // --- INTERNAL STATE (Auto-managed) ---
    private Rigidbody rb;
    private Transform visualMesh;
    private Transform cameraPivot;
    private Vector3 pivotOffset;

    private float currentSpeed = 0f;
    private float currentRudder = 0f;
    private float targetRudder = 0f;
    
    private float camYaw = 0f;
    private float camPitch = 25f;
    private float currentDistance;
    private float targetDistance;
    private Vector3 camPosVel;
    private Vector3 camRotVel;
    private bool isTactical = false;
    private float currentFOV;

    // AUTO-SETUP MAGIC: Runs when you first attach the script in the Editor
    void Reset()
    {
        AutoFindVisualMesh();
        AutoCalculatePivot();
    }

    void Awake()
    {
        // 1. Auto-configure Rigidbody
        rb = GetComponent<Rigidbody>();
        rb.linearDamping = 0.5f;
        rb.angularDamping = 1f;
        rb.constraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

        // 2. Auto-find references if user didn't assign them
        if (visualMeshOverride != null) visualMesh = visualMeshOverride;
        else AutoFindVisualMesh();

        if (cameraPivotOverride != null) cameraPivot = cameraPivotOverride;
        else AutoCalculatePivot();

        // 3. Init Camera
        currentDistance = defaultDistance;
        targetDistance = defaultDistance;
        currentFOV = baseFOV;
        camYaw = transform.eulerAngles.y;
    }

    void AutoFindVisualMesh()
    {
        // Finds the first child with a MeshRenderer to apply tilting to
        MeshRenderer[] renderers = GetComponentsInChildren<MeshRenderer>();
        if (renderers.Length > 0)
        {
            visualMesh = renderers[0].transform;
        }
        else
        {
            visualMesh = transform; // Fallback to root if no children
        }
    }

    void AutoCalculatePivot()
    {
        // Calculates the exact 3D center of all meshes in the ship
        Bounds bounds = new Bounds(transform.position, Vector3.zero);
        bool foundBounds = false;
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            bounds.Encapsulate(r.bounds);
            foundBounds = true;
        }
        
        if (foundBounds)
        {
            // Store the offset from the ship's root to its visual center
            pivotOffset = bounds.center - transform.position;
            // Create a hidden pivot point if needed, or just use the offset
            // For simplicity, we'll just use the offset in the camera math
        }
    }

    void Update()
    {
        HandleCameraInput();
        UpdateCamera();
        UpdateVisualTilt();
    }

    void FixedUpdate()
    {
        HandlePhysics();
    }

    #region 1. CRUISER PHYSICS
    void HandlePhysics()
    {
        // Throttle
        float throttle = Input.GetAxis("Vertical");
        if (throttle > 0) currentSpeed = Mathf.MoveTowards(currentSpeed, maxSpeed, acceleration * Time.fixedDeltaTime);
        else if (throttle < 0) currentSpeed = Mathf.MoveTowards(currentSpeed, -maxReverseSpeed, deceleration * Time.fixedDeltaTime);
        else currentSpeed = Mathf.MoveTowards(currentSpeed, 0, 2f * Time.fixedDeltaTime);

        // Steering
        float steer = Input.GetAxis("Horizontal");
        targetRudder = steer * maxTurnRate;
        currentRudder = Mathf.MoveTowards(currentRudder, targetRudder, rudderResponsiveness * maxTurnRate * Time.fixedDeltaTime);

        // Speed penalty & movement requirement
        float speedRatio = Mathf.Abs(currentSpeed) / maxSpeed;
        float turnPenalty = 1f - (speedRatio * highSpeedTurnPenalty);
        float moveFactor = Mathf.Clamp01(Mathf.Abs(currentSpeed) / 5f);
        
        float finalTurn = currentRudder * turnPenalty * moveFactor;
        rb.angularVelocity = new Vector3(0, finalTurn * Mathf.Deg2Rad, 0);

        // Movement & Drift
        Vector3 forward = transform.forward;
        Vector3 right = transform.right;
        Vector3 newVel = forward * currentSpeed;

        // Inject lateral drift (the "carve")
        float drift = (currentRudder / maxTurnRate) * speedRatio * driftFactor;
        newVel += right * (currentSpeed * drift);

        // Water resistance on lateral slide
        float latSpeed = Vector3.Dot(rb.linearVelocity, right);
        newVel -= right * (latSpeed * 3f) * Time.fixedDeltaTime;

        rb.linearVelocity = newVel;
    }
    #endregion

    #region 2. VISUAL TILT (Heeling/Pitching)
    void UpdateVisualTilt()
    {
        if (visualMesh == null) return;

        float speedRatio = Mathf.Abs(currentSpeed) / maxSpeed;
        float targetRoll = -currentRudder * heelMultiplier * speedRatio;
        float targetPitch = -Input.GetAxis("Vertical") * pitchMultiplier;

        // Smoothly tilt the visual mesh (leaves the physics collider flat)
        Quaternion targetRot = Quaternion.Euler(targetPitch, 0f, targetRoll);
        visualMesh.localRotation = Quaternion.Slerp(visualMesh.localRotation, targetRot, 4f * Time.fixedDeltaTime);
    }
    #endregion

    #region 3. CINEMATIC CAMERA
    void HandleCameraInput()
    {
        if (Input.GetMouseButton(1)) // Right click to orbit
        {
            camYaw += Input.GetAxis("Mouse X") * cameraOrbitSpeed;
            camPitch -= Input.GetAxis("Mouse Y") * cameraOrbitSpeed;
        }

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        targetDistance -= scroll * cameraZoomSpeed;
        targetDistance = Mathf.Clamp(targetDistance, minDistance, isTactical ? 150f : defaultDistance + 20f);

        if (Input.GetKeyDown(KeyCode.C) || Input.GetKeyDown(KeyCode.Tab))
        {
            isTactical = !isTactical;
            targetDistance = isTactical ? tacticalDistance : defaultDistance;
        }

        camPitch = Mathf.Clamp(camPitch, minPitch, maxPitch);
        currentDistance = Mathf.Lerp(currentDistance, targetDistance, Time.deltaTime * 4f);
    }

    void UpdateCamera()
    {
        if (Camera.main == null) return;

        // Calculate the actual world-space pivot point (Ship center + offset)
        Vector3 actualPivot = transform.position + pivotOffset + (transform.up * 2f); // Lifted slightly above deck

        // Dynamic FOV based on speed
        float speedRatio = Mathf.Abs(currentSpeed) / maxSpeed;
        currentFOV = Mathf.Lerp(currentFOV, Mathf.Lerp(baseFOV, maxSpeedFOV, speedRatio), Time.deltaTime * 2f);
        Camera.main.fieldOfView = currentFOV;

        // Orbit Math
        Quaternion orbitRot = Quaternion.Euler(camPitch, camYaw, 0);
        Vector3 idealPos = actualPivot - (orbitRot * Vector3.forward * currentDistance);

        // Add rotational lag (camera sweeps behind the ship during turns)
        float yawOffset = rb.angularVelocity.y * cameraRotationalLag * 50f; 
        idealPos += transform.right * -yawOffset;

        // CAMERA COLLISION (Prevents clipping through the ship)
        Ray ray = new Ray(actualPivot, idealPos - actualPivot);
        if (Physics.SphereCast(ray, 2f, out RaycastHit hit, currentDistance))
        {
            idealPos = ray.GetPoint(hit.distance - 2f);
        }

        // Smooth Position
        Camera.main.transform.position = Vector3.SmoothDamp(Camera.main.transform.position, idealPos, ref camPosVel, 0.25f);

        // Smooth Rotation
        Vector3 targetEuler = new Vector3(camPitch, camYaw, 0);
        Vector3 smoothEuler = Vector3.SmoothDamp(Camera.main.transform.eulerAngles, targetEuler, ref camRotVel, 0.15f);
        smoothEuler.z = 0;
        Camera.main.transform.rotation = Quaternion.Euler(smoothEuler);
    }
    #endregion
}