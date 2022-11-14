using UnityEngine;


public class VolumeSlider : MonoBehaviour
{
	[SerializeField] private UnityEngine.UI.Slider m_slider;
	[SerializeField] private UnityEngine.Audio.AudioMixer m_mixer;

	[SerializeField] private string m_paramName = "VolumeDB"; // TODO: split param names for prefs/slider since one is a percentage and the other in decibels?

	[SerializeField] UnityEngine.Events.UnityEvent m_volumeSetEvent; // NOTE that this could be handled by m_slider.onValueChanged except that then any SFX would play when setting the initial value on scene load


	private bool m_initialized = false;


	// NOTE that this assumes that this component will not be on an object that is initially disabled
	private void Start()
	{
		if (PlayerPrefs.HasKey(m_paramName))
		{
			// NOTE that setting m_slider.value will invoke SetVolume() via m_slider.onValueChanged
			m_slider.value = PlayerPrefs.GetFloat(m_paramName);
		}
		m_initialized = true;
	}


	public void SetVolume(float pct)
	{
		m_mixer.SetFloat(m_paramName, PercentToDecibels(pct));
		PlayerPrefs.SetFloat(m_paramName, pct);
		if (m_initialized)
		{
			m_volumeSetEvent.Invoke(); // NOTE that this is AFTER m_mixer.SetFloat() in order to use the updated volume in any SFX
		}
	}


	private float PercentToDecibels(float pct) => Mathf.Log10(pct) * 20.0f; // for formula source, see https://johnleonardfrench.com/the-right-way-to-make-a-volume-slider-in-unity-using-logarithmic-conversion/
}
