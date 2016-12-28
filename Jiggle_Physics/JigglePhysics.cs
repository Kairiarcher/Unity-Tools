using System.Collections;
using UnityEngine;

/* 
 Description:
    -allow joint/bone in a 3D object jiggle dynamically in unity engine as the object is in motion, without actual animations.
    -allow joint/bone to be grabbed by VIVE controllers and jiggled dynamically after release
    -video demo link:
    https://www.youtube.com/watch?v=HfM3aIFqN0g
 Instruction:
    -For this jiggle physics script to work, it is required to be placed on a end bone/joint.
    -Change the "pointingDirection" in unity inspector to initialize where the bone should point at the start of the game
    -Change the rest of public values in inspector to fit your object's scale (Since this physics was developed for VR environment, every value was default set according to unity scale)
*/

public class JigglePhysics : MonoBehaviour
{
    const int numFrames = 15;

    // physics related inputs
    public Vector3 pointingDirection = new Vector3();
    public float jiggleSpeed;
    public float ignoredDeltaMagnitude = 0.02f;
    [Range(1, 10)]
    public float bounceFadeOff;
    [Range(0, 1)]
    public float elasticity;

    // Debug mode related
    public bool debugMode = true;
    public float targetCubeSize = 0.05f;
    [Tooltip("how far ahead to place your debug objects")]
    [Range(0, 10)]
    public float frontTargetPlacement = 0.2f;
    [Tooltip("how sensitive it is when detecting the movement")]
    public float inMotionThreshhold;

    // Use this for initialization
    bool boneInMotion;
    bool bounceReturnPositionCaptured;
    bool isBeingGrabbed = false;
    bool startPosCaptured = false;
    bool stopPosCaptured = false;
    float nextTriggerTimer = 0;
    GameObject handObj;
    GameObject parentGameObject;
    GameObject staticTarget;
    GameObject staticBone;
    int bounceCounter = 0;
    int itHasBeenStaticForFrames = 0;

    SteamVR_Controller.Device handController = null;
    Vector3 bounceTargetPosition; // the position of the target that will be placed in front of your bone/joint
    Vector3 boneVeloctiy;
    Vector3 distanceTraveled;
    Vector3 dynamicTargetPosition;
    Vector3 movementStartPosition;
    Vector3 movementStopPosition;
    Vector3 previousPosition;
    Vector3 staticTargetPosition;
    Vector3 toParentDeltaPosition;

    void Start()
    {
        // record initial values, that the object will return to when static
        parentGameObject = transform.parent.gameObject;
        toParentDeltaPosition = transform.position - parentGameObject.transform.position;
        Vector3 boneStaticPosition = parentGameObject.transform.position + toParentDeltaPosition;
        previousPosition = boneStaticPosition;
        staticTargetPosition = boneStaticPosition + pointingDirection;

        // defines where the object should be pointing to when static
        staticTarget = GameObject.CreatePrimitive(PrimitiveType.Cube);
        staticTarget.transform.localScale = new Vector3(targetCubeSize, targetCubeSize, targetCubeSize);
        staticTarget.transform.name = transform.name + "target";
        staticTarget.transform.position = staticTargetPosition;
        staticTarget.transform.SetParent(parentGameObject.transform);

        // defines where the object should be positioned when static
        staticBone = GameObject.CreatePrimitive(PrimitiveType.Cube);
        staticBone.transform.localScale = new Vector3(targetCubeSize, targetCubeSize, targetCubeSize);
        staticBone.transform.name = transform.name + "bone";
        staticBone.transform.position = transform.position;
        staticBone.transform.SetParent(parentGameObject.transform);

        // starting position
        dynamicTargetPosition = staticTargetPosition;
        bounceTargetPosition = staticTargetPosition;
    }

    void Update()
    {
        DrawDebugTargetLine();
        CheckIfObjectInMotion();
        MoveDynamicTarget();
        CalculateBoneTravelDistance();
    }

    void LateUpdate()
    {
        MoveBone();
        previousPosition = staticBone.transform.position;
    }

    public void OnTriggerStay(Collider colliObj)
    {
        // check if controllers are in contact with objects
        var wand = colliObj.GetComponent<Wand>();
        if (wand)
        {
            handController = wand.device;
            handObj = colliObj.gameObject;
            CheckGripButtonIsHoldingDown();
        }
    }

    // if debug mode is on, 2 cubes (for the static position of bone and static position of the target) and 2 lines 
    // (for bounce position and dynamic position of the target) will be drawn on the editor mode.
    void DrawDebugTargetLine()
    {
        if (debugMode)
        {
            Debug.DrawRay(dynamicTargetPosition, Vector3.down, Color.red);
            Debug.DrawRay(bounceTargetPosition, Vector3.forward, Color.blue);
        }
        else
        {
            staticTarget.SetActive(false);
            staticBone.SetActive(false);
        }
    }

    void CheckIfObjectInMotion()
    {
        boneVeloctiy = (staticBone.transform.position - previousPosition) / Time.deltaTime;
        itHasBeenStaticForFrames++;

        if (boneVeloctiy.magnitude < inMotionThreshhold &&
            itHasBeenStaticForFrames > numFrames ||
            !isBeingGrabbed)
        {
            boneVeloctiy = Vector3.zero;
            boneInMotion = false;
        }
        else if (boneVeloctiy.magnitude > inMotionThreshhold ||
                isBeingGrabbed)
        {
            boneInMotion = true;
            itHasBeenStaticForFrames = 0;
        }
    }

    void CheckGripButtonIsHoldingDown()
    {
        if (handController.GetPress(SteamVR_Controller.ButtonMask.Grip))
        {
            isBeingGrabbed = true;
            bounceTargetPosition = handObj.transform.position;
            dynamicTargetPosition = bounceTargetPosition;
        }
        else if (handController.GetPressUp(SteamVR_Controller.ButtonMask.Grip))
        {
            isBeingGrabbed = false;
        }
    }

    void MoveDynamicTarget()
    {
        // this only calculates where the dynamic target is, but doesn't actually move the bone yet!!
        if (!isBeingGrabbed)
        {
            if (boneInMotion)
            {
                // dynamic target will be chasing after the object, which will cause a delay effect
                dynamicTargetPosition = Vector3.Lerp(dynamicTargetPosition, staticTarget.transform.position, Time.deltaTime);
            }

            if (!boneInMotion)
            {
                // a bounce will be calculated and depends on your "bounceFadeOff" input. 
                // the dynamic target will be moving towards that bounce with the jiggleSpeed. 
                // after the first rebound, the second bounce will also be calculated.
                // This continues until the dynamic target reachs the static position 
                CalculateBounceReturnPosition();
                dynamicTargetPosition = Vector3.Lerp(dynamicTargetPosition, bounceTargetPosition, jiggleSpeed * Time.deltaTime);
            }
        }
        else
        {
            // if the bone was grabbed and released, the position where you released the bone will be the first bounce position. After that it will keep bouncing until it reached the static position
            CalculateBounceReturnPosition();
            dynamicTargetPosition = Vector3.Lerp(dynamicTargetPosition, bounceTargetPosition, jiggleSpeed * Time.deltaTime);
        }
    }

    void MoveBone()
    {
        // move the bone as the dynamic target moves with some delay.
        // in case of grabbing, it will just move simultanteous
        // rotation of the bone
        transform.LookAt(dynamicTargetPosition);
        //position of the bone
        Vector3 dynamicTargetPositionWithDelta = dynamicTargetPosition - (staticTarget.transform.position - staticBone.transform.position);

        if (!isBeingGrabbed)
        {
            transform.position = Vector3.MoveTowards(transform.position, dynamicTargetPositionWithDelta, (elasticity));
        }
        else
        {
            transform.position = Vector3.MoveTowards(transform.position, dynamicTargetPositionWithDelta, (1));
        }
    }

    void CalculateBoneTravelDistance()
    {
        // calculates the distance that the object traveled and capture the position where the movement started and ended.
        if (!isBeingGrabbed)
        {
            if (startPosCaptured)
            {
                // check if going opposite direction then reset start position
                CheckIfBoneGoingOppositeDirection();
            }

            if (boneInMotion && !startPosCaptured)
            {
                // record start position
                movementStartPosition = staticBone.transform.position;
                startPosCaptured = true;
                stopPosCaptured = false;
                bounceCounter = 0;
            }
            else if (!boneInMotion && !stopPosCaptured && startPosCaptured)
            {
                // record end position
                movementStopPosition = staticBone.transform.position;
                stopPosCaptured = true;
                startPosCaptured = false;
                distanceTraveled = movementStopPosition - movementStartPosition;
            }
        }
        else if (isBeingGrabbed)
        {
            // when in grab mode, the start position will be where you release the grib and the end position 
            // will be the bone static position, which will automatically triggers bounce calculation.
            if (!boneInMotion)
            {
                movementStartPosition = bounceTargetPosition;
            }
        }
    }

    void CheckIfBoneGoingOppositeDirection()
    {
        // check whether the object is going in the opposited direction, then reset the start position.
        const float everySecond = 0.05f; 
        if (Time.time > nextTriggerTimer)
        {
            nextTriggerTimer = Time.time + everySecond;
            float currentPosToStartDelta = (staticBone.transform.position - movementStartPosition).magnitude;
            float previousPosToStartDelta = (previousPosition - movementStartPosition).magnitude;

            if (currentPosToStartDelta < previousPosToStartDelta &&
                (currentPosToStartDelta - previousPosToStartDelta) < -(inMotionThreshhold) &&  //delta must be bigger than minimum value;
                (staticBone.transform.position - previousPosition).magnitude > inMotionThreshhold)  //prevent hand shaking
            {
                movementStartPosition = staticBone.transform.position;
            }
        }
    }

    void CalculateBounceReturnPosition()
    {
        if (!isBeingGrabbed)
        {
            if (!boneInMotion && bounceCounter <= 0 && stopPosCaptured)
            {
                // calculates the first bounce after the motion stopped
                bounceCounter += 1;
                bounceReturnPositionCaptured = true;
                bounceTargetPosition = staticTarget.transform.position + (distanceTraveled / bounceFadeOff);
            }

            if ((bounceTargetPosition - staticTarget.transform.position).magnitude > ignoredDeltaMagnitude // bounce not stabled
                && (dynamicTargetPosition - bounceTargetPosition).magnitude < ignoredDeltaMagnitude) //dynamic has reached peak
            {
                // calculates any bounce afterward, until it reachs the static position. 
                // The amount of bounces depends on your bounceFadeOff
                bounceCounter += 1;
                distanceTraveled = staticTarget.transform.position - bounceTargetPosition;
                bounceTargetPosition = staticTarget.transform.position + (distanceTraveled / bounceFadeOff);
            }
        }
    }
}
