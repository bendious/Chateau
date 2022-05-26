using UnityEngine;


[DisallowMultipleComponent]
public class ColorRandomizer : MonoBehaviour
{
	public Color m_colorMin = Color.gray;
	public Color m_colorMax = Color.white;


	// NOTE that this has to be Awake() rather than Start() since some other components do color-based logic after spawning
	private void Awake()
	{
		GetComponent<SpriteRenderer>().color = Utility.ColorRandom(m_colorMin, m_colorMax); // TODO: more deliberate choice?
	}
}
