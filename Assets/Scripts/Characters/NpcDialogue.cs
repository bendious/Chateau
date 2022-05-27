using UnityEngine;


[CreateAssetMenu]
public class NpcDialogue : ScriptableObject
{
	public WeightedObject<DialogueController.Line[]>[] m_dialogue;
}
