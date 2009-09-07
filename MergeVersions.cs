/*
 * MergeVersions.cs
 *
 * Author(s)
 * 	Fabian Kneissl <fabian@kneissl-web.net>
 *  Stephane Delcroix <stephane@delcroix.org>
 *
 * This is free software. See COPYING for details
 */

using System;
using System.IO;
using System.Collections.Generic;

using Gtk;

using FSpot;
using FSpot.Extensions;
using FSpot.UI.Dialog;
using FSpot.Utils;
using Mono.Unix;

namespace MergeVersionsExtension
{
	public class MergeVersions : ICommand
	{
		public void Run (object o, EventArgs e)
		{
			Console.WriteLine ("EXECUTING MERGE VERSIONS EXTENSION");

			if (ResponseType.Ok != HigMessageDialog.RunHigConfirmation (
				MainWindow.Toplevel.Window,
				DialogFlags.DestroyWithParent,
				MessageType.Warning,
				"Merge Versions",
				"This operation will merge versions of the same image as one unique image. Version names are guessed using the standard F-Spot naming scheme.\n\nNote: only enabled for photos with a maximum of 4 additional versions and the 'Modified' status (not 'Modified by' or custom version names) right now.",
				"Do it now"))
				return;

			IList<MergeRequest> merge_requests = new List<MergeRequest> ();

			String [] version_names = new String[] {
					Catalog.GetPluralString ("Modified", "Modified ({0})", 1),
					String.Format( Catalog.GetPluralString ("Modified", "Modified ({0})", 2), 2),
					String.Format( Catalog.GetPluralString ("Modified", "Modified ({0})", 3), 3),
					String.Format( Catalog.GetPluralString ("Modified", "Modified ({0})", 4), 4)
			};

			PhotoStore photo_store = FSpot.Core.Database.Photos;
			foreach ( IBrowsableItem photo in photo_store.Query ( "SELECT * FROM photos " ) ) {
				Photo p = (Photo) photo;

				if (!ImageFile.IsJpeg (p.Name))
					continue;

				bool not_found = false;
				string version_path = p.VersionUri (Photo.OriginalVersionId).AbsoluteUri;

				for (int j = 0; j < version_names.Length; j++) {
					if (p.Name.Contains(version_names[j])) {
						not_found = true;
						string original_path = version_path.Replace (" ("+version_names[j]+")", "");
						original_path = original_path.Replace ("%20("+version_names[j].Replace(" ","%20")+")", "");
						Uri original_uri = new Uri (original_path);
						if (version_path.Equals(original_path))
							continue;

						Photo [] originals = Core.Database.Photos.Query(original_uri);
						if (originals != null && originals.Length == 1) {
							Console.WriteLine("Modified version found: {0}", version_path);
							Console.WriteLine("  => Merging with original: {0}", original_uri);
							merge_requests.Add (new MergeRequest (originals[0], p, version_names[j]));
							not_found = false;
							break;
						}
					}
				}

				if (not_found) {
					Console.WriteLine("Modified version found: {0}", version_path);
					Console.WriteLine("  => No original found. Not merging");
				}
			}

			if (merge_requests.Count == 0)
				return;

			Console.WriteLine();
			Console.WriteLine("Starting merge process");
			Console.WriteLine();

			foreach (MergeRequest mr in merge_requests)
				mr.Merge ();

			MainWindow.Toplevel.UpdateQuery ();
		}

		class MergeRequest
		{
			Photo original;
			Photo version;
			string version_name;

			public MergeRequest (Photo original, Photo version, string version_name)
			{
				this.original = original;
				this.version = version;
				this.version_name = version_name;
			}

			public void Merge ()
			{
				Console.WriteLine ("Merging {0} and {1}", original.VersionUri (Photo.OriginalVersionId), version.VersionUri (Photo.OriginalVersionId));
				foreach (uint version_id in version.VersionIds) {
					string name = version.GetVersion (version_id).Name;
					try {
						original.DefaultVersionId = original.CreateReparentedVersion (version.GetVersion (version_id) as PhotoVersion, version_id == Photo.OriginalVersionId);
						if (version_id == Photo.OriginalVersionId)
							original.RenameVersion (original.DefaultVersionId, version_name);
						else
							original.RenameVersion (original.DefaultVersionId, name);
					} catch (Exception e) {
						Console.WriteLine (e);
					}
				}
				original.AddTag (version.Tags);
				uint [] version_ids = version.VersionIds;
				Array.Reverse (version_ids);
				foreach (uint version_id in version_ids) {
					try {
						version.DeleteVersion (version_id, true, true);
					} catch (Exception e) {
						Console.WriteLine (e);
					}
				}
				original.Changes.DataChanged = true;
				Core.Database.Photos.Commit (original);
				Core.Database.Photos.Remove (version);
			}
		}
	}
}
