using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;


// edited from https://dastmo.com/tutorials/unity-input-system-persistent-rebinds/
public class ControlsRemapping : MonoBehaviour
{
	[SerializeField] private InputActionAsset m_controls;


	public static InputActionAsset Controls;
	public static Action<InputAction> SuccessfulRebinding;

	public static Dictionary<string, string> OverridesDictionary = new();


	private const string m_filename = "/controlsOverrides.dat";


	private void Awake()
	{
		if (Controls != null)
		{
			Destroy(this);
			return;
		}

		Controls = m_controls;

		if (File.Exists(Application.persistentDataPath + m_filename))
		{
			LoadControlOverrides();
		}
	}


	public static void RemapKeyboardAction(InputAction actionToRebind, int targetBinding)
	{
		actionToRebind.PerformInteractiveRebinding(targetBinding)
			.WithControlsHavingToMatchPath("<Keyboard>")
			.WithBindingGroup("Keyboard")
			.WithCancelingThrough("<Keyboard>/escape")
			.OnCancel(operation => SuccessfulRebinding?.Invoke(null))
			.OnComplete(operation => {
				operation.Dispose();
				AddOverrideToDictionary(actionToRebind.id, actionToRebind.bindings[targetBinding].effectivePath, targetBinding);
				SaveControlOverrides();
				SuccessfulRebinding?.Invoke(actionToRebind);
			})
			.Start();
	}

	// TODO: remove/combine?
	public static void RemapGamepadAction(InputAction actionToRebind, int targetBinding)
	{
		actionToRebind.PerformInteractiveRebinding(targetBinding)
			.WithControlsHavingToMatchPath("<Gamepad>")
			.WithBindingGroup("Gamepad")
			.WithCancelingThrough("<Keyboard>/escape")
			.OnCancel(operation => SuccessfulRebinding?.Invoke(null))
			.OnComplete(operation => {
				operation.Dispose();
				AddOverrideToDictionary(actionToRebind.id, actionToRebind.bindings[targetBinding].effectivePath, targetBinding);
				SaveControlOverrides();
				SuccessfulRebinding?.Invoke(actionToRebind);
			})
			.Start();
	}


	private static void AddOverrideToDictionary(Guid actionId, string path, int bindingIndex)
	{
		string key = string.Format("{0} : {1}", actionId.ToString(), bindingIndex);

		if (OverridesDictionary.ContainsKey(key))
		{
			OverridesDictionary[key] = path;
		}
		else
		{
			OverridesDictionary.Add(key, path);
		}
	}

	// TODO: use Unity functions?
	private static void SaveControlOverrides()
	{
		FileStream file = new(Application.persistentDataPath + m_filename, FileMode.OpenOrCreate);
		BinaryFormatter bf = new();
		bf.Serialize(file, OverridesDictionary);
		file.Close();
	}

	private static void LoadControlOverrides()
	{
		if (!File.Exists(Application.persistentDataPath + m_filename))
		{
			return;
		}

		FileStream file = new(Application.persistentDataPath + m_filename, FileMode.Open);
		BinaryFormatter bf = new();
		OverridesDictionary = bf.Deserialize(file) as Dictionary<string, string>;
		file.Close();

		foreach (KeyValuePair<string, string> item in OverridesDictionary)
		{
			// TODO: sanitize input?
			string[] split = item.Key.Split(new string[] { " : " }, StringSplitOptions.None);
			Guid id = Guid.Parse(split[0]);
			int index = int.Parse(split[1]);
			Controls./*asset.*/FindAction(id).ApplyBindingOverride(index, item.Value);
		}
	}
}
