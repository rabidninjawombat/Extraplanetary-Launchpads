/*
This file is part of Extraplanetary Launchpads.

Extraplanetary Launchpads is free software: you can redistribute it and/or
modify it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

Extraplanetary Launchpads is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with Extraplanetary Launchpads.  If not, see
<http://www.gnu.org/licenses/>.
*/
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

using KSP.IO;

namespace ExtraplanetaryLaunchpads {

public class ExRecycler : PartModule, IModuleInfo
{
	double busyTime;
	bool recyclerActive;
	[KSPField] public float RecycleRate = 1.0f;
	[KSPField (guiName = "State", guiActive = true)] public string status;

	public override string GetInfo ()
	{
		return "Recycler:\n" + String.Format ("rate: {0:G2}t/s", RecycleRate);
	}

	public string GetPrimaryField ()
	{
		return String.Format ("Recycling Rate: {0:G2}t/s", RecycleRate);
	}

	public string GetModuleTitle ()
	{
		return "EL Recycler";
	}

	public Callback<Rect> GetDrawModulePanelCallback ()
	{
		return null;
	}

	public bool CanRecycle (Vessel vsl)
	{
		if (vsl == null || vsl == vessel) {
			// avoid oroboro
			return false;
		}
		foreach (Part p in vsl.parts) {
			// Don't try to recycle an asteroid or any vessel attached to
			// an asteroid.
			if (p.Modules.Contains ("ModuleAsteroid")) {
				return false;
			}
		}
		return true;
	}

	public void OnTriggerStay (Collider col)
	{
		if (!recyclerActive
			|| Planetarium.GetUniversalTime () <= busyTime
			|| !col.CompareTag ("Untagged")
			|| col.gameObject.name == "MapOverlay collider")	// kethane
			return;
		Part p = col.attachedRigidbody.GetComponent<Part>();
		//Debug.Log (String.Format ("[EL] OnTriggerStay: {0}", p));
		if (p != null && CanRecycle (p.vessel)) {
			float mass;
			if (p.vessel.isEVA) {
				mass = RecycleKerbal (p.vessel.GetVesselCrew ()[0], p);
			} else {
				mass = RecycleVessel (p.vessel);
			}
			busyTime = Planetarium.GetUniversalTime () + mass / RecycleRate;
		}
	}

	private float ReclaimResource (string resource, double amount,
								  string vessel_name, string name=null)
	{
		PartResourceDefinition res_def;
		res_def = PartResourceLibrary.Instance.GetDefinition (resource);
		VesselResources recycler = new VesselResources (vessel);

		if (res_def == null) {
			return 0;
		}

		if (name == null) {
			name = resource;
		}
		double remain = amount;
		// any resources that can't be pumped or don't flow just "evaporate"
		// FIXME: should this be a little smarter and convert certain such
		// resources into rocket parts?
		if (res_def.resourceTransferMode != ResourceTransferMode.NONE
			&& res_def.resourceFlowMode != ResourceFlowMode.NO_FLOW) {
			remain = recycler.TransferResource (resource, amount);
		}
		Debug.Log (String.Format ("[EL] {0}-{1}: {2} taken {3} reclaimed, {4} lost", vessel_name, name, amount, amount - remain, remain));
		return (float) (amount * res_def.density);
	}

	static string FormatTime (double time)
	{
		int iTime = (int) time % 3600;
		int seconds = iTime % 60;
		int minutes = (iTime / 60) % 60;
		int hours = (iTime / 3600);
		return hours.ToString ("D2") + ":" + minutes.ToString ("D2")
			+ ":" + seconds.ToString ("D2");
	}

	public float RecycleKerbal (ProtoCrewMember crew, Part part)
	{
		// idea and numbers taken from Kethane
		if (crew.isBadass && part != null) {
			part.explosionPotential = 10000;
			FlightGlobals.ForceSetActiveVessel (this.vessel);
		}
		string message = crew.name + " was mulched";
		ScreenMessages.PostScreenMessage (message, 30.0f, ScreenMessageStyle.UPPER_CENTER);
		if (part != null) {
			FlightLogger.eventLog.Add ("[" + FormatTime (part.vessel.missionTime) + "] " + message);
			part.explode ();
		}

		float mass = 0;
		mass += ReclaimResource (ExSettings.KerbalRecycleTarget,
								 ExSettings.KerbalRecycleAmount, crew.name);
		mass += ReclaimResource (ExSettings.HullRecycleTarget, 1, crew.name);
		return mass;
	}

	public float RecycleVessel (Vessel v)
	{
		float ConversionEfficiency = 0.8f;
		double amount;
		VesselResources scrap = new VesselResources (v);

		PartResourceDefinition rp_def;
		string target_resource = ExSettings.HullRecycleTarget;
		rp_def = PartResourceLibrary.Instance.GetDefinition (target_resource);

		if (FlightGlobals.ActiveVessel == v)
			FlightGlobals.ForceSetActiveVessel (this.vessel);
		float mass = 0;
		foreach (var crew in v.GetVesselCrew ()) {
			mass += RecycleKerbal (crew, null);
		}
		foreach (string resource in scrap.resources.Keys) {
			amount = scrap.ResourceAmount (resource);
			mass += ReclaimResource (resource, amount, v.name);
			scrap.TransferResource (resource, -amount);
		}
		float hull_mass = v.GetTotalMass ();
		amount = hull_mass * ConversionEfficiency / rp_def.density;
		mass += ReclaimResource (target_resource, amount, v.name, String.Format ("hull({0})", target_resource));
		v.Die ();
		return mass;
	}

	[KSPEvent (guiActive = true, guiName = "Activate Recycler", active = true)]
	public void Activate ()
	{
		recyclerActive = true;
		Events["Activate"].active = false;
		Events["Deactivate"].active = true;
	}

	[KSPEvent (guiActive = true, guiName = "Deactivate Recycler",
	 active = false)]
	public void Deactivate ()
	{
		recyclerActive = false;
		Events["Activate"].active = true;
		Events["Deactivate"].active = false;
	}

	public override void OnLoad (ConfigNode node)
	{
		if (CompatibilityChecker.IsWin64 ()) {
			Events["Activate"].active = false;
			Events["Deactivate"].active = false;
			recyclerActive = false;
			return;
		}
		Deactivate ();
	}

	public override void OnUpdate ()
	{
		if (Planetarium.GetUniversalTime () <= busyTime) {
			status = "Busy";
		} else if (recyclerActive) {
			status = "Active";
		} else {
			status = "Inactive";
		}
	}
}

}
