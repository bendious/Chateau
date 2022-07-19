using UnityEngine;


[DisallowMultipleComponent]
public class ColorRandomizer : MonoBehaviour
{
	[SerializeField] private Color m_colorMin = Color.gray;
	[SerializeField] private Color m_colorMax = Color.white;

	[SerializeField] private bool m_proportional;


	// NOTE that this has to be Awake() rather than Start() since some other components do color-based logic after spawning
	private void Awake()
	{
		Color color = Utility.ColorRandom(m_colorMin, m_colorMax, m_proportional); // TODO: more deliberate choice?
		foreach (SpriteRenderer r in GetComponentsInChildren<SpriteRenderer>())
		{
			if (r.GetComponentInParent<ColorRandomizer>() != this)
			{
				continue; // skip descendants w/ a more immediate ColorRandomizer
			}
			r.color = color;
		}
	}
}
