using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RenderTexScript : MonoBehaviour {

    public RenderTexture renderTex;

	// Use this for initialization
	void Start () {
		
	}
	
	// Update is called once per frame
	void Update () {
		
	}

     private void OnRenderObject() {

        //m_blendMat.SetTexture("_AccumTex", m_accumTex);
        //m_blendMat.SetTexture("_RevealageTex", m_revealageTex);

        Graphics.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), renderTex);

        //Graphics.DrawTexture(new Rect(0, 0, Screen.width / 2, Screen.height), m_blendMat);  // be aware that this call seems to fuck the particles up, if the texture is shown on a plane it looks different.
    }
}
