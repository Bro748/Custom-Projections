using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using RWCustom;
using UnityEngine;

namespace CustomProjections
{
	[BepInPlugin("bro.customprojections", "CustomProjections", "0.1.0")]    // (GUID, mod name, mod version)
	public class CustomProjections : BaseUnityPlugin
	{
		public void OnEnable()
		{
			/* This is called when the mod is loaded. */


			On.RainWorld.Start += RainWorld_Start;
		}

		private void RainWorld_Start(On.RainWorld.orig_Start orig, RainWorld self)
		{
			orig(self);

			IL.OverseerHolograms.OverseerImage.ctor += OverseerImage_ctor;

			IL.OverseerHolograms.OverseerImage.HoloImage.ctor += HoloImage_PatchPROJ;
			IL.OverseerHolograms.OverseerImage.HoloImage.DrawSprites += HoloImage_PatchPROJ;

		}

		private void HoloImage_PatchPROJ(ILContext il)
		{
			//replaces all mentions of "STR_PROJ" with LoadProjPng()
			//Should instead replace it with LoadProjPng((self.imageOwner as OverseerHolograms.OverseerImage)?.room.abstractRoom.name)
			//but idk how to do that

			var c = new ILCursor(il);

			if (c.TryGotoNext(moveType: MoveType.Before,
				i => i.MatchLdstr("STR_PROJ")
			))
			{
				c.MoveAfterLabels();
				c.Emit(OpCodes.Call, typeof(CustomProjections).GetMethod("LoadProjPng", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public));
				c.Remove();
			}
			else { Debug.LogError("Something went wrong"); }
		}

		private void OverseerImage_ctor(ILContext il)
		{
			var c = new ILCursor(il);

			int namelocal = 0;
			if (c.TryGotoNext(moveType: MoveType.After,
				i => i.MatchLdarg(out _),
				i => i.MatchLdfld<UpdatableAndDeletable>("room"),
				i => i.MatchCallOrCallvirt<Room>("get_abstractRoom"),
				i => i.MatchLdfld<AbstractRoom>("name"),
				i => i.MatchStloc(out namelocal)
			))
			{
				c.Emit(OpCodes.Ldarg_0);
				c.Emit(OpCodes.Ldloc, namelocal);

				c.EmitDelegate<Func<OverseerHolograms.OverseerImage, string, bool>>((OverseerHolograms.OverseerImage self, string name) =>
				{
					//set the roomName for use in other classes where it's less accessible (bad)
					Globals.roomName = name;

					//if there's a _PROJ file for the current room, load it and read the lines
					if (File.Exists(LoadProjFile(name, "txt")))
					{
						//default values
						self.timeOnEachImage = 25;
						self.showTime = 150;
						int num = 0;
						//generic integer, used to convert from 
						int item = 0;

						//check each line in room_PROJ.txt individually
						foreach (string text in File.ReadAllText(LoadProjFile(name, "txt")).Split(new string[]
						{ Environment.NewLine }, StringSplitOptions.None))
						{
							//if the line is a vanilla ImageID enum, then add that to the list

							if (Enum.IsDefined(typeof(OverseerHolograms.OverseerImage.ImageID), text))
							{
								item = (int)Enum.Parse(typeof(OverseerHolograms.OverseerImage.ImageID), text);
								self.images.Add((OverseerHolograms.OverseerImage.ImageID)item);
							}

							//if the formatting is correct, set timeOnEachImage to the value specified

							else if (text.Substring(Math.Max(text.Length - 15, 0), Math.Min(15, text.Length)) == "timeOnEachImage")
							{ self.timeOnEachImage = int.Parse(text.Substring(0, text.Length - 15)); }

							//if the formatting is correct, set timeOnEachImage to the value specified

							else if (text.Substring(Math.Max(text.Length - 8, 0), Math.Min(8, text.Length)) == "showTime")
							{ self.showTime = int.Parse(text.Substring(0, text.Length - 8)); }

							//if none of those match, check to see if it's a custom enum.
							//If it is, add ImageID of the equivalent value of the custom enum to the list
							else
							{
								item = CustomEnum(text);
								if (item >= 0)
								{
									self.images.Add((OverseerHolograms.OverseerImage.ImageID)item);
								}
							}
							num++;
						}
						//if a _PROJ file for the current room WAS detected, then activate the bool to skip over the hardcoded list
						return true;
					}
					return false;
				});

				var skipped = c.DefineLabel();
				c.Emit(OpCodes.Brtrue, skipped);

				if (c.TryGotoNext(moveType: MoveType.Before,
				i => i.MatchLdarg(out _),
				i => i.MatchLdfld<Overseer>("AI"),
				i => i.MatchLdfld<OverseerAI>("communication"),
				i => i.MatchCallOrCallvirt<OverseerCommunicationModule>("get_GuideState"),
				i => i.MatchCallOrCallvirt<PlayerGuideState>("get_guideSymbol")
				))
				{
					c.MarkLabel(skipped);
				}
				else { Debug.LogError("Something went terribly wrong"); }
			}
			else { Debug.LogError("Something went wrong"); }


		}

		
		public static class EnumExt_CustomProjections
		{
			//adds to the vanilla ImageID enum so that it has the max amount of 25. mostly useless, tbh
			public static OverseerHolograms.OverseerImage.ImageID Misc_1;
			public static OverseerHolograms.OverseerImage.ImageID Misc_2;
			public static OverseerHolograms.OverseerImage.ImageID Misc_3;
			public static OverseerHolograms.OverseerImage.ImageID Misc_4;
		}


		
		public static string LoadProjFile(string fileName, string Type)
		{
			//returns the path to any proj files, relative to the pack folder of the current room
			//Resources\Futile\Resources\Projections\<fileName>_PROJ.<Type>
			string result = string.Concat(new object[]
			{
				CustomRegions.Mod.CRExtras.BuildPath(
					PackFromRoom(Globals.roomName),
					CustomRegions.Mod.CRExtras.CustomFolder.Resources),


					"Projections",
					Path.DirectorySeparatorChar,
					fileName,
					"_PROJ.",
					Type
			});

			return result;
		}
		
		public static string LoadProjPng()
		{
			//returns the name of the image proj file specified as the first line of info_PROJ.txt
			//if the file doesn't exist or if none are specified, it returns the vanilla file "STR_PROJ"

			
			string result = "STR_PROJ";
			//get the path to Info_PROJ.txt
			string Info = LoadProjFile("Info", "txt");
			//if the file exists, read the first line and if that refers to a real file, then return that file
			if (File.Exists(Info))
			{
				string[] array = File.ReadAllText(Info).Split(new string[]
				{Environment.NewLine}, 
				StringSplitOptions.None);

				if (File.Exists(LoadProjFile(array[0], "png")))
				{
					result = array[0] + "_PROJ";
				}
			}
			return result;
		}


		public static int CustomEnum(string read)
		{
			//if the info file exists, return the index of the string (which is -1 if it doesn't exist)
			if (File.Exists(LoadProjFile("info", "txt")))
			{
				string[] custEnum = File.ReadAllText(LoadProjFile("info", "txt")).Split(new string[]
							{ Environment.NewLine }, 
							StringSplitOptions.None);

				int index = Array.IndexOf(custEnum, read, 1);
				return index - 1;
			}

			//return -1 if the file doesn't exist
			else {
				return -1;
			}

		}

		public static string PackFromRoom(string roomname)
		{
			//returns the pack name, or null if the room is vanilla

			string[] path = WorldLoader.FindRoomFileDirectory(roomname, true).Split(Path.DirectorySeparatorChar);
			int index = Array.IndexOf(path, "CustomResources", 0);

			if (index != -1)
			{
			
				return path[index + 1];
			}
			else
			{ return null; }

		}
		
		public static class Globals
			{
			//stores the room name globally so that it's useable by PackFromRoom, though it's passed in from LoadProjFile
			//currently *very* inefficient, but I've spent way too long trying to pass the room name into HoloImage, and I've given up for now

			public static string roomName;
			}
		public static void IggyDebug(string print)
        {
			//for debug purposes, not currently used
			if (!File.Exists(Custom.RootFolderDirectory()+"IggyDebug.txt"))
			{ File.Create(Custom.RootFolderDirectory() + "IggyDebug.txt"); }
			File.AppendAllText(Custom.RootFolderDirectory() + "IggyDebug.txt", print+Environment.NewLine);
			
        }
	}
}
