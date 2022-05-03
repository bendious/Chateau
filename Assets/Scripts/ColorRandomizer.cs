using UnityEngine;


[DisallowMultipleComponent]
public class ColorRandomizer : MonoBehaviour
{
	public float m_colorMin = 0.5f;
	public float m_colorMax = 1.0f;


	// NOTE that this has to be Awake() rather than Start() since some other components do color-based logic after spawning
	private void Awake()
	{
		GetComponent<SpriteRenderer>().color = Utility.ColorRandom(m_colorMin, m_colorMax); // TODO: more deliberate choice?
	}
}
