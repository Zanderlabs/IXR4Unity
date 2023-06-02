using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LSL;

public class IXR_VR_Controller : MonoBehaviour
{
    public static IXR_VR_Controller Instance;

    [Header("BCI and simulation")]
    public float focusLevel = 0.5f;

    private float currentMovementFocus = 0.0f;
    private float currentBCIFocus = 0.5f;

    public bool doSimulateBCI = true;
    public enum BCISimulationMethod
    {
        random, constant
    }
    public BCISimulationMethod BCISimulation;
    public bool useMotionForSimulation = true;

    private float BCISimulationIntervall = 0.1f;

    private int pastMovementVectorsAmount = 240;
    private float movementFocusScale = 0.2f;

    [Header("HMD and hands for simuation")]
    public GameObject HMD;
    public GameObject leftHand;
    public GameObject rightHand;

    private Vector3 lastHMDpos;
    private Quaternion lastHMDrot;
    private Vector3 lastLeftHandPos;
    private Quaternion lastLeftHandRot;
    private Vector3 lastRightHandPos;
    private Quaternion lastRightHandRot;

    private float currentHMDposVel;
    private float currentHMDrotVel;
    private float[] pastHMDposVel;
    private float[] pastHMDrotVel;
    private float currentHMDposFocus = 0;
    private float currentHMDrotFocus = 0;
    private float meanHMDPosVel;
    private float meanHMDRotVel;

    private float currentleftHandposVel;
    private float currentleftHandrotVel;
    private float[] pastleftHandposVel;
    private float[] pastleftHandrotVel;
    private float currentleftHandposFocus = 0;
    private float currentleftHandrotFocus = 0;
    private float meanleftHandPosVel;
    private float meanleftHandRotVel;

    private float currentrightHandposVel;
    private float currentrightHandrotVel;
    private float[] pastrightHandposVel;
    private float[] pastrightHandrotVel;
    private float currentrightHandposFocus = 0;
    private float currentrightHandrotFocus = 0;
    private float meanrightHandPosVel;
    private float meanrightHandRotVel;

    private float lastBCISimulationChange = 0;
    private float rotThreshold = 5;
    private float posThreshold = 0.02f;

    [Header("receiving data")]
    public string streamName = "BrainPower"; //naming is essential here! otherwise the inlet can't be found
    private ContinuousResolver resolver;
    private StreamInlet inlet;
    private float[] sample;
    private float disconnectTimer;
    private float disconnectTimeLimit = 1;

    [Header("pushing event markers")]
    public string senderStreamName = "IXREventMarker";
    private string streamType = "Markers";
    private static StreamOutlet outlet;
    private static string[] sampleString = { "" };



    private void Awake()
    {
        //as the VR BCI Controller should not be destroyed when loading another scene,
        //and to avoid having multiple VR BCI Controllers in one scene, a Singleton pattern will
        //make it easier to always have only one (and the same) Instance in a scene, while also
        //referencing data from this script in others is very simple
        //using the same method for other single Instances like the GameController or GameStateManager
        if (Instance != null && Instance != this)
        {
            Destroy(this.gameObject); //only destroying the script is not enough as the Game Object still exists then
        }
        else
        {
            Instance = this;
        }
    }

    // Start is called before the first frame update
    void Start()
    {
        // this ensures that the instance of this script does not get destroyed on scene change (works in relation with the Singleton pattern)
        DontDestroyOnLoad(this.gameObject);

        lastBCISimulationChange = Time.time;

        // initialize motion for simulation if HMD, leftHand, and rightHand are assigned
        if (!(HMD == null || leftHand == null || rightHand == null))
        {
            lastHMDpos = HMD.transform.position;
            lastHMDrot = HMD.transform.rotation;
            lastLeftHandPos = leftHand.transform.position;
            lastLeftHandRot = leftHand.transform.rotation;
            lastRightHandPos = rightHand.transform.position;
            lastRightHandRot = rightHand.transform.rotation;
        }
        else
        {
            Debug.LogWarning("HMD or hands are not assigned, cannot simulate focus based on motion!");
        }

        // initialize vectors
        pastHMDposVel = new float[pastMovementVectorsAmount];
        pastHMDrotVel = new float[pastMovementVectorsAmount];
        pastleftHandposVel = new float[pastMovementVectorsAmount];
        pastleftHandrotVel = new float[pastMovementVectorsAmount];
        pastrightHandposVel = new float[pastMovementVectorsAmount];
        pastrightHandrotVel = new float[pastMovementVectorsAmount];

        //Sender logic initialization
        if (!doSimulateBCI)
        {
            Debug.Log("Using real IXR focus stream!");
            var hashStreamName = Hash128.Compute(senderStreamName);
            var hashStreamType = Hash128.Compute(streamType);
            int id = gameObject.GetInstanceID();
            var hashObjectID = Hash128.Compute(id.ToString());
            var hash = (hashStreamName, hashStreamType, hashObjectID).GetHashCode();
            StreamInfo streamInfo = new StreamInfo(senderStreamName, streamType, 1, LSL.LSL.IRREGULAR_RATE,
                    channel_format_t.cf_string, hash.ToString());
            outlet = new StreamOutlet(streamInfo);
            Debug.Log("LSL event marker stream created");
        }
    }

    // Update is called once per frame
    void Update()
    {
        if (doSimulateBCI)
        {
            if (Time.time - lastBCISimulationChange >= BCISimulationIntervall)
            {
                SimulateBCI(BCISimulation);
            }

            if (useMotionForSimulation)
            {
                UpdateMovement();

                focusLevel = GetSimulatedFocusValue();
            }
            else
            {
                focusLevel = currentBCIFocus;
            }
        }
        else
        { 
                PullDataFromInlet();
        }
    }

    private void SimulateBCI(BCISimulationMethod method)
    {
        switch (method)
        {
            case BCISimulationMethod.random:
                currentBCIFocus += UnityEngine.Random.Range(-0.1f, 0.1f);
                currentBCIFocus = Mathf.Clamp(currentBCIFocus, 0.25f, 0.8f);
                break;
            case BCISimulationMethod.constant:
                currentBCIFocus = 0.55f;
                break;
            default:
                Debug.LogError("Incorrect BCI simulation method! Must be 'random' or 'constant'!");
                break;
        }

        lastBCISimulationChange = Time.time;
    }

    private void PullDataFromInlet()
    {
        // if there is no inlet, search for one
        if (inlet == null)
        {
            //Debug.Log("Resolving LSL stream");
            // the streamName is of great importance because it is looking for channels with that exact name here!
            resolver = new ContinuousResolver("name", streamName);
            var results = resolver.results();

            // create an inlet if there is one
            if (results.Length > 0)
            {
                inlet = new StreamInlet(results[0]);
                inlet.open_stream();
                Debug.Log("IXR stream inlet created");
            }
            // if no inlet can be found, switch to simulated brainpower and delete the current resolver
            else
            {
                // Debug.LogWarning("no inlet found! simulating brainpower...");
                resolver = null;

                if (Time.time - lastBCISimulationChange >= BCISimulationIntervall) SimulateBCI(BCISimulationMethod.random);

                UpdateMovement();

                focusLevel = GetSimulatedFocusValue();
            }
        }

        // if there is an inlet, pull samples
        if (inlet != null)
        {
            sample = new float[1];
            double lastTimeStamp = inlet.pull_sample(sample, 0.0f);

            // if the sample's value is not zero, set it as the focusLevel
            if (sample[0] != 0)
            {
                if (disconnectTimer > 0) disconnectTimer = 0;
                focusLevel = sample[0];

                // Debug.Log("current IXR BCI focus: " + focusLevel);

            }
            // if the sample's value is zero, it may indicate a disconnection to the stream.
            // for each value that equals zero, add time to a disconnect timer, to initiate a disconnect if they keep appearing (reset the timer if value != 0)
            else
            {
                disconnectTimer += Time.deltaTime;

                if (disconnectTimer >= disconnectTimeLimit)
                {
                    Debug.LogWarning("DISCONNECT! Simulating BCI Focus now!");

                    disconnectTimer = 0;

                    // set the starting value for simulated brainpower
                    currentBCIFocus = 0.5f;

                    inlet.close_stream();
                    inlet = null;
                }
            }
        }
    }

    private void UpdateMovement()
    {
        // Check if HMD, leftHand, and rightHand are assigned
        if (HMD == null || leftHand == null || rightHand == null) return;

        // Continue with the rest of your code if all objects are assigned...

        currentHMDposVel = Vector3.Distance(lastHMDpos, HMD.transform.position) / Time.deltaTime;

        for (int i = 0; i <= pastHMDposVel.Length - 2; i++)
        {
            pastHMDposVel[i] = pastHMDposVel[i + 1];
        }
        pastHMDposVel[pastHMDposVel.Length - 1] = currentHMDposVel;

        currentHMDrotVel = Quaternion.Angle(lastHMDrot, HMD.transform.rotation) / Time.deltaTime;

        for (int i = 0; i <= pastHMDrotVel.Length - 2; i++)
        {
            pastHMDrotVel[i] = pastHMDrotVel[i + 1];
        }
        pastHMDrotVel[pastHMDrotVel.Length - 1] = currentHMDrotVel;

        // update left hand values
        if (leftHand == null) return;

        currentleftHandposVel = Vector3.Distance(lastLeftHandPos, leftHand.transform.position) / Time.deltaTime;

        for (int i = 0; i <= pastleftHandposVel.Length - 2; i++)
        {
            pastleftHandposVel[i] = pastleftHandposVel[i + 1];
        }
        pastleftHandposVel[pastleftHandposVel.Length - 1] = currentleftHandposVel;

        currentleftHandrotVel = Quaternion.Angle(lastLeftHandRot, leftHand.transform.rotation) / Time.deltaTime;

        for (int i = 0; i <= pastleftHandrotVel.Length - 2; i++)
        {
            pastleftHandrotVel[i] = pastleftHandrotVel[i + 1];
        }
        pastleftHandrotVel[pastleftHandrotVel.Length - 1] = currentleftHandrotVel;

        // update right hand values
        if (rightHand == null) return;

        currentrightHandposVel = Vector3.Distance(lastRightHandPos, rightHand.transform.position) / Time.deltaTime;

        for (int i = 0; i <= pastrightHandposVel.Length - 2; i++)
        {
            pastrightHandposVel[i] = pastrightHandposVel[i + 1];
        }
        pastrightHandposVel[pastrightHandposVel.Length - 1] = currentrightHandposVel;

        currentrightHandrotVel = Quaternion.Angle(lastRightHandRot, rightHand.transform.rotation) / Time.deltaTime;

        for (int i = 0; i <= pastrightHandrotVel.Length - 2; i++)
        {
            pastrightHandrotVel[i] = pastrightHandrotVel[i + 1];
        }
        pastrightHandrotVel[pastrightHandrotVel.Length - 1] = currentrightHandrotVel;


        // update last values to be current
        lastHMDpos = HMD.transform.position;
        lastHMDrot = HMD.transform.rotation;
        lastLeftHandPos = leftHand.transform.position;
        lastLeftHandRot = leftHand.transform.rotation;
        lastRightHandPos = rightHand.transform.position;
        lastRightHandRot = rightHand.transform.rotation;
    }

    public float GetSimulatedFocusValue()
    {
        // compute HMD values
        foreach (float item in pastHMDposVel)
        {
            meanHMDPosVel += item;
        }
        meanHMDPosVel = meanHMDPosVel / pastHMDposVel.Length;

        foreach (float item in pastHMDrotVel)
        {
            meanHMDRotVel += item;
        }
        meanHMDRotVel = meanHMDRotVel / pastHMDrotVel.Length;

        // sigmoid formulas for vel:
        currentHMDposFocus = -(movementFocusScale / 3) / (1 + (Mathf.Exp((-4 / posThreshold) * meanHMDPosVel + 4))) + ((movementFocusScale / 3) / 2);
        currentHMDrotFocus = -(movementFocusScale / 3) / (1 + (Mathf.Exp((-4 / rotThreshold) * meanHMDRotVel + 4))) + ((movementFocusScale / 3) / 2);

        // compute left hand values
        foreach (float item in pastleftHandposVel)
        {
            meanleftHandPosVel += item;
        }
        meanleftHandPosVel = meanleftHandPosVel / pastleftHandposVel.Length;

        foreach (float item in pastleftHandrotVel)
        {
            meanleftHandRotVel += item;
        }
        meanleftHandRotVel = meanleftHandRotVel / pastleftHandrotVel.Length;

        // sigmoid formulas for vel:
        currentleftHandposFocus = -(movementFocusScale / 3) / (1 + (Mathf.Exp((-4 / posThreshold) * meanleftHandPosVel + 4))) + ((movementFocusScale / 3) / 2);
        currentleftHandrotFocus = -(movementFocusScale / 3) / (1 + (Mathf.Exp((-4 / rotThreshold) * meanleftHandRotVel + 4))) + ((movementFocusScale / 3) / 2);

        // compute right hand values
        foreach (float item in pastrightHandposVel)
        {
            meanrightHandPosVel += item;
        }
        meanrightHandPosVel = meanrightHandPosVel / pastrightHandposVel.Length;

        foreach (float item in pastrightHandrotVel)
        {
            meanrightHandRotVel += item;
        }
        meanrightHandRotVel = meanrightHandRotVel / pastrightHandrotVel.Length;

        // sigmoid formulas for vel:
        currentrightHandposFocus = -(movementFocusScale / 3) / (1 + (Mathf.Exp((-4 / posThreshold) * meanrightHandPosVel + 4))) + ((movementFocusScale / 3) / 2);
        currentrightHandrotFocus = -(movementFocusScale / 3) / (1 + (Mathf.Exp((-4 / rotThreshold) * meanrightHandRotVel + 4))) + ((movementFocusScale / 3) / 2);

        // combine movement values
        currentMovementFocus = currentHMDposFocus + currentHMDrotFocus +
            currentleftHandposFocus + currentleftHandrotFocus +
            currentrightHandposFocus + currentrightHandrotFocus;

        // overall combine and clamp focus
        focusLevel = currentBCIFocus + currentMovementFocus;
        focusLevel = Mathf.Clamp(focusLevel, 0.05f, 1);

        return focusLevel;
    }

    // Push event markers through the outlet
    public void SendEventMarker(string eventName, string eventTask)
    {
        // the Python code for LSL expects that a message (messageToSend) is "name;task", with the name and task seperated by a semicolon!
        // e.g. messageToSend = "event;senddata";
        string messageToSend = eventName + ";" + eventTask;

        Debug.Log("Sending Event marker: " + messageToSend);

        if (outlet == null) return;

        sampleString[0] = messageToSend;
        outlet.push_sample(sampleString);
    }
}
