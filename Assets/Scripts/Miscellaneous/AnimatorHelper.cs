using UnityEngine;


[RequireComponent(typeof(Animator), typeof(AudioSource))]
public class AnimatorHelper : MonoBehaviour
{
	private AudioSource m_audioSource;


	private void Start()
	{
		m_audioSource = GetComponent<AudioSource>();
	}


	[System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "called by animation triggers")]
	private void ProcessAnimEvent(AnimationEvent evt)
	{
		Debug.Assert(evt.objectReferenceParameter != null);
		if (evt.objectReferenceParameter is AudioClip clip)
		{
			m_audioSource.PlayOneShot(clip);
		}
		else if (evt.objectReferenceParameter is AudioCollection collection)
		{
			m_audioSource.PlayOneShot(collection.Random());
		}
		else
		{
			Debug.Assert(false);
		}
	}
}
