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
using System.Text.RegularExpressions;

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
			Log.Information ("Executing MergeVersions extension");

			IList<MergeRequest> merge_requests = new List<MergeRequest> ();

			string pat = @"^(.*) \(([A-Za-z0-9_ -]+)\)(\.[A-Za-z0-9]{3,4})$";
			// Groups		1	   2				  3
			Regex pattern = new Regex(pat);

			PhotoStore photo_store = FSpot.Core.Database.Photos;
			foreach ( IBrowsableItem photo in photo_store.Query ( "SELECT * FROM photos " ) )
			{
				Photo p = (Photo) photo;

				string version_path = p.VersionUri (Photo.OriginalVersionId).ToString();
				Match m = pattern.Match(version_path);

				if (m.Success)
				{
					Log.Information ("Modified version found: \"{0}\"", version_path);

					string original_path = m.Groups[1].Value + m.Groups[3].Value;
					string version_name = m.Groups[2].Value;
					Uri original_uri = new Uri (original_path);

					Photo [] originals = Core.Database.Photos.Query(original_uri);
					if (originals != null && originals.Length == 1)
					{
						if (DateTime.Compare(p.Time, originals[0].Time) == 0)
						{
							Log.Information ("  => Merging with original: \"{0}\" as \"{1}\"", original_uri, version_name);
							merge_requests.Add (new MergeRequest (originals[0], p, version_name));
						}
						else
						{
							Log.Information ("  => Original \"{0}\" does not have same date/time. Not merging.", original_uri);
						}
					}
					else
					{
						Log.Information ("  => No original found in \"{0}\". Not merging.", original_uri);
					}
				}
			}

			if (merge_requests.Count == 0)
				return;

			if (ResponseType.Ok == HigMessageDialog.RunHigConfirmation (
				MainWindow.Toplevel.Window,
				DialogFlags.DestroyWithParent,
				MessageType.Question,
				"Merge Versions",
				"This operation will merge versions of the same photo as one unique photo. " + merge_requests.Count + " versions have been found. Merge now?",
				"OK"))
			{
				Log.Information ("Starting merge process");

				foreach (MergeRequest mr in merge_requests)
					mr.Merge ();

				MainWindow.Toplevel.UpdateQuery ();
			}
			Log.Information ("Finished executing MergeVersions extension");
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
				Log.Information ("Merging \"{0}\" and \"{1}\"", original.VersionUri (Photo.OriginalVersionId), version.VersionUri (Photo.OriginalVersionId));
				foreach (uint version_id in version.VersionIds) {
					string name = version.GetVersion (version_id).Name;
					try {
						original.DefaultVersionId = original.CreateReparentedVersion (version.GetVersion (version_id) as PhotoVersion, version_id == Photo.OriginalVersionId);
						if (version_id == Photo.OriginalVersionId)
							original.RenameVersion (original.DefaultVersionId, version_name);
						else
							original.RenameVersion (original.DefaultVersionId, name);
					} catch (Exception e) {
						Log.Exception (e);
					}
				}
				original.AddTag (version.Tags);
				uint [] version_ids = version.VersionIds;
				Array.Reverse (version_ids);
				foreach (uint version_id in version_ids) {
					try {
						version.DeleteVersion (version_id, true, true);
					} catch (Exception e) {
						Log.Exception (e);
					}
				}
				original.Changes.DataChanged = true;
				Core.Database.Photos.Commit (original);
				Core.Database.Photos.Remove (version);
			}
		}
	}
}
