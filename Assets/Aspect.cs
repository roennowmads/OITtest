using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Aspect : MonoBehaviour {

    public Camera mainCam;

	// Use this for initialization
	void Start () {
		
	}
	
	// Update is called once per frame
	void Update () {                   
        transform.localScale = new Vector3(mainCam.aspect, 1.0f, 1.0f);
	}
}
