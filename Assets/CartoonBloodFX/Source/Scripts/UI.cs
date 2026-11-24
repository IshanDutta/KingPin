using UnityEngine;
using System.Collections;

namespace HWREfx
{
	public class UI : MonoBehaviour
	{
		public GameObject[] particleSpanwner;
		public int indexSpawn = 0;
		public bool epictime;
		public Transform ground;
		private float timetemp = 0;
        public float CameraSpeed = 2;

		void Start ()
		{
			timetemp = Time.time;
		}

		void Update ()
		{
			this.transform.Rotate (Vector3.up, Time.deltaTime * CameraSpeed);

			if (Input.GetButtonDown ("Fire1")) {
				var ray = GameObject.Find ("Main Camera").GetComponent<Camera>().ScreenPointToRay (Input.mousePosition);
				RaycastHit hit;

				if (Physics.Raycast (ray, out hit, 100)) {
					if (hit.transform.tag == "ground") {
						if (particleSpanwner.Length > 0) {
							SpawnParticle (hit.point);
						}
					}
				}

			}
			if (epictime) {
				if (Time.time > timetemp + 0.05f) {
					timetemp = Time.time;
					SpawnParticle (new Vector3 (Random.Range (-30, 30), 0, Random.Range (-30, 30)));
					indexSpawn = Random.Range (0, particleSpanwner.Length);
				}
			}
		}

		void SpawnParticle (Vector3 position,float showTime = 3)
		{
			Vector3 offset = Vector3.zero;
            GameObject sp = GameObject.Instantiate (particleSpanwner [indexSpawn], position + offset, Quaternion.identity);
            GameObject.Destroy(sp.gameObject, showTime);
        }

		void OnGUI ()
		{
			if (particleSpanwner.Length > 0) {
				if (GUI.Button (new Rect (10, 10, 150, 30), "Prev")) {
					indexSpawn--;
					if (indexSpawn < 0) {
						indexSpawn = particleSpanwner.Length - 1;
					}
				}
				GUI.Label (new Rect (10, 40, 1000, 30), "Particle Name: " + particleSpanwner [indexSpawn].name.ToString ());
				if (GUI.Button (new Rect (170, 10, 150, 30), "Next")) {
					indexSpawn++;
					if (indexSpawn >= particleSpanwner.Length) {
						indexSpawn = 0;
					}
				}

				if (GUI.Button (new Rect (350, 10, 120, 30), "Ground")) {
					if (ground.gameObject.GetComponent<Renderer>().enabled) {
						ground.gameObject.GetComponent<Renderer>().enabled = false;
					} else {
						ground.gameObject.GetComponent<Renderer>().enabled = true;
					}
				}

				if (GUI.Button (new Rect (480, 10, 120, 30), "Show time")) {
					if (epictime) {
						epictime = false;
					} else {
						epictime = true;
					}
				}
			}

		}
	}
}
