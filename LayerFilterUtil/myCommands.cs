// (C) Copyright 2015 by  
//
using System;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using System.Text;

using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.LayerManager;
using System.Text.RegularExpressions;

// This line is not mandatory, but improves loading performances
[assembly: CommandClass(typeof(LayerFilterUtil.MyCommands))]

namespace LayerFilterUtil
{

	// This class is instantiated by AutoCAD for each document when
	// a command is called by the user the first time in the context
	// of a given document. In other words, non static data in this class
	// is implicitly per-document!
	public class MyCommands
	{

		const int FUNCTION = 0;
		const int F_NAME = 1;
		const int F_TYPE = 2;
		const int F_PARENT = 3;
		const int F_EXPRESSION = 4;
		const int F_LAYERS = 4;
		const int F_MIN = 5;

		static int TabLevel = 0;
		static int NestDepth = 0;

		ResultBuffer resbufOut;

		Document doc;
		Database db;
		Editor ed;


		[LispFunction("LayerFilterUtil")]
		public ResultBuffer LayerFilterUtil(ResultBuffer args) // This method can have any name
		{

			// initalize global vars
			doc = Application.DocumentManager.MdiActiveDocument;	// get reference to the current document
			db = doc.Database;										// get reference to the current dwg database
			ed = doc.Editor;											// get reference to the current editor (text window)

			resbufOut = new ResultBuffer();

			TypedValue[] tvArgs;

			// reset for each usage
			NestDepth = 0;

			// process the args buffer
			// if passed is null, no good return null
			if (args != null)
			{
				// convert the args buffer into an array
				tvArgs = args.AsArray();

				// if the first argument is not text
				// return null
				if (tvArgs[0].TypeCode != (int)LispDataType.Text)
				{
					return null;
				}

				//displayArgs(tvArgs);
				//return null;

			}
			else
			{
				DisplayUsage();
				return null; ;
			}


			// access to the collection of layer filters
			LayerFilterTree lfTree = db.LayerFilters;
			LayerFilterCollection lfCollect = lfTree.Root.NestedFilters;

			switch (((string)tvArgs[0].Value).ToLower())
			{
				case "list":
					// validate the args buffer, 2nd level - there can be only a single argument
					if (tvArgs.Length == 1)
					{
						return ListFilters(lfCollect);
					}

					return null;
					break;
				case "find":
					// finding 1 existing layer filter - did only 2 args get
					// provided and is the 2nd arg a text arg?  if yes, proceed
					if (tvArgs.Length == 2 && tvArgs[1].TypeCode == (int)LispDataType.Text)
					{
						// search for the layer filter 
						return FindFilter(lfCollect, ((string)tvArgs[1].Value));
					}

					return null;
					break;
				case "add":

					// add a new layer filter to the layer filter collection
					// allow the filter to be added as a nested filter to another filter
					// except that any filter that cannot be deleted, cannot have nested filters
					// parameter options:
					// first	(idx = 0 FUNCTION) parameter == "add"
					// second	(idx = 1 F_NAME) parameter == "filter name" (cannot be duplicate)
					// third	(idx = 2 F_TYPE) parameter == "filter type" either "property" or "group" (case does not matter)
					// fifth	(idx = 3 F_PARENT) parameter == "parent name" or ("" or nil) for no parent name
					// fourth	(idx = 4 F_EXPRESSION) parameter == "filter expression" for property filter
					// fourth	(idx = 4 F_LAYERS) parameter == "layer ids" for a group filter
					

					// possible add options:
					// add a property filter to the root of the collection
					// add a property filter to another layer filter (property or group)
					// add a group filter to the root of the collection
					// add a group filter to to another group filter (cannot be a property filter)

					return AddFilter(lfTree, lfCollect, tvArgs);
					break;
				case "delete":
					// deleting existing layer filter(s) - only 2 args allowed
					// special case filter to delete = "*" means all of the existing filters
					// except those filters marked as "cannot delete"
					// provided and is the 2nd arg a text arg?  if yes, proceed

					if ((string)tvArgs[1].Value != "*")
					{
						if (tvArgs.Length == 2 && tvArgs[1].TypeCode == (int)LispDataType.Text)
						{

							return DeleteFilter(lfTree, lfCollect, (string)tvArgs[1].Value);

						}
					}
					else
					{
						// special case, name to delete is *
						// delete all filters that are not marked as cannot delete

						NestDepth = 0;

						List<LayerFilter> lfList = SearchFilters(lfCollect, allowDelete: false);

						if (lfList != null && lfList.Count > 0)
						{
							return DeleteFilters(lfTree, lfCollect, lfList);
						}

					}

		
					resbufOut = null;
					break;
				case "usage":
					DisplayUsage();
					resbufOut = null;
					break;
			}

			return resbufOut;
		}

		private ResultBuffer ListFilters(LayerFilterCollection lfCollect)
		{
			return BuildResBuffer(SearchFilters(lfCollect));
		}


		/// <summary>
		/// Finds a single filter from the Layer Filter Collection<para />
		/// Returns a Result Buffer with the Layer Filter information
		/// </summary>
		/// <param name="lfCollect"></param>
		/// <param name="searchName"></param>
		/// <returns></returns>
		private ResultBuffer FindFilter(LayerFilterCollection lfCollect, string searchName)
		{
			List<LayerFilter> lFilter = SearchFilters(lfCollect, Name: searchName);

			if (lFilter.Count <= 0)
			{
				return null;
			}

			return BuildResBuffer(lFilter);
		}

		/// <summary>
		/// Add filter
		/// </summary>
		/// <param name="lfTree"></param>
		/// <param name="lfCollect"></param>
		/// <returns></returns>
		private ResultBuffer AddFilter(LayerFilterTree lfTree, LayerFilterCollection lfCollect, TypedValue[] tvArgs)
		{

			// validate parameters
			// by getting to this point, first parameter validated


			// minimum of 5 parameters and 
			// parameters 1 & 2 must be text
			// parameter 3 must be text or nil
			// or new filter cannot already exist
			if (tvArgs.Length < F_MIN
				|| tvArgs[F_NAME].TypeCode != (int)LispDataType.Text
				|| tvArgs[F_TYPE].TypeCode != (int)LispDataType.Text
				|| (tvArgs[F_PARENT].TypeCode != (int)LispDataType.Text
				&& tvArgs[F_PARENT].TypeCode != (int)LispDataType.Nil)
				|| SearchOneFilter(lfCollect, Name: (string)tvArgs[F_NAME].Value) != null)
			{
				return null;
			}

			// parameter 4+ must be text
			for (int i = F_EXPRESSION; i < tvArgs.Length; i++)
			{
				if (tvArgs[i].TypeCode != (int)LispDataType.Text)
					return null;
			}


			// *** parameters basic validation complete ***

			// at this point, we have the correct type of args (text or nil)
			switch (((string)tvArgs[F_TYPE].Value).ToLower())
			{
				// add a property filter
				case "property":

					// two cases - add to root of tree or add to existing
					// if tvArgs[F_PARENT] == "" or tvArgs[F_PARENT] == nil, add to tree root
					// if tvArgs[F_PARENT] == string, add to existing parent

					if (tvArgs[F_PARENT].TypeCode == (int)LispDataType.Nil || ((string)tvArgs[F_PARENT].Value).Length == 0)
					{
						// already checked that new filter does not exist - ok to proceed
						// add a property filter with the parent being null
						if (AddPropertyFilter(lfTree, lfCollect, (string)tvArgs[F_NAME].Value, null, (string)tvArgs[F_EXPRESSION].Value))
						{
							// filter added, return the data about the new filter
							return FindFilter(lfCollect, (string)tvArgs[F_NAME].Value);
						}

					}
					else
					{
						// bit more complex - add a layer filter to an existing layer filter (nested layer filter)
						// parent filter must exist
						List<LayerFilter> lfList = SearchOneFilter(lfCollect, (string)tvArgs[F_PARENT].Value);

						if (lfList != null)
						{
							// already checked that the new filter does not exist - ok to proceed
							// add a property filter using a parent
							if (AddPropertyFilter(lfTree, lfCollect, (string)tvArgs[F_NAME].Value, lfList[0], (string)tvArgs[F_EXPRESSION].Value))
							{
								// filter added, return data about the filter
								return FindFilter(lfCollect, (string)tvArgs[F_NAME].Value);
							}
						}
					}

					// get here, something did not work - return nil
					return null;
					break;
				case "group":

					// two cases - have or have not parent

					if (tvArgs[F_PARENT].TypeCode == (int)LispDataType.Nil || ((string)tvArgs[F_PARENT].Value).Length == 0)
					{
						// simple case - add group filter to the tree root

						// args at this point:
						// FUNCTION = "add" - already verified
						// F_NAME = filter name - already verified
						// F_TYPE = "group" - already verified
						// F_PARENT = filter parent is blank or nil - already verified
						// F_LAYERS = begining of the list of layers to include in the group filter
						ObjectIdCollection layIds = new ObjectIdCollection();

						// store the aray of layers into a list
						List<string> layerNames = getLayersFromArg(tvArgs);

						if (layerNames.Count != 0)
						{
							// process the list of layers and get their layer ids
							layIds = getLayerIds(layerNames);

							if (layIds.Count != 0)
							{
								// now have a list of layer id's for the layer group
								// now add the layer filter group and its layer id's

								if (AddGroupFilter(lfTree, lfCollect,
									(string)tvArgs[F_NAME].Value, null, layIds))
								{
									return FindFilter(lfCollect, (string)tvArgs[F_NAME].Value);
								}
							}
						}
						// provide the return information
						return null;
						break;
					}
					else
					{
						// complex case - add group filter to an existing filter
						// existing filter cannot be a property filter

						// args at this point:
						// FUNCTION = "add" - already verified
						// F_NAME = filter name - already verified (data type)
						// F_TYPE = "group" - already verified
						// F_PARENT = filter parent is not blank - already verified
						// F_LAYERS = begining of the list of layers to include in the group filter

						List<LayerFilter> lfList = SearchOneFilter(lfCollect, (string)tvArgs[F_PARENT].Value);

						// store the aray of layers into a list
						 List<string> layerNames = getLayersFromArg(tvArgs);

						if (layerNames.Count != 0 && lfList != null)
						{

							// process the list of layers and get their layer ids
							 ObjectIdCollection layIds = getLayerIds(layerNames);

							if (layIds.Count != 0)
							{
								// now have a list of layer id's for the layer group
								// now add the layer filter group and its layer id's

								if (AddGroupFilter(lfTree, lfCollect,
									(string)tvArgs[F_NAME].Value, lfList[0], layIds))
								{
									return FindFilter(lfCollect, (string)tvArgs[F_NAME].Value);
								}
							}
						}
						// provide the return information
						return null;
						break;
					}
					break;
			}
			return null;
		}



		/// <summary>
		/// Add a property filter
		/// </summary>
		/// <param name="lfTree">Filter Tree</param>
		/// <param name="lfCollect">Filter Collection</param>
		/// <param name="Name">New Filter Name</param>
		/// <param name="Parent">Existing Parent under which to add the new filter (null if add to root</param>
		/// <param name="Expression">The filter expression</param>
		/// <returns></returns>
		private bool AddPropertyFilter(LayerFilterTree lfTree, LayerFilterCollection lfCollect, 
			string Name, LayerFilter Parent, string Expression) 
		{

			if (Parent != null && Parent.AllowNested != true)
			{
				return false;
			}

			// the layer filter collection is lfCollect
			// since this may cause an error, use try catch
			try
			{
				// make an empty layer filter
				LayerFilter lf = new LayerFilter();

				// add the layer filter data
				lf.Name = Name;
				lf.FilterExpression = Expression;


				if (Parent == null)
				{
					// add the filter to the collection
					lfCollect.Add(lf);
				} 
				else 
				{
					// add the layer filter as a nested filter
					Parent.NestedFilters.Add(lf);
				}

				// add the collection back to the data base
				db.LayerFilters = lfTree;

				// update the layer palette to show the
				// layer filter changes
				refreshLayerManager();
			}
			catch (System.Exception)
			{
				// something did not work, return false
				return false;
			}

			return true;
		}

		private bool AddGroupFilter(LayerFilterTree lfTree, LayerFilterCollection lfCollect,
			string Name, LayerFilter Parent, ObjectIdCollection layIds )
		{
			// validate that this group filter is allowed to be added - 
			// if this is to be added to a Parent filter, the parent filter
			// must allow nesting
			// cannot be an ID (property) filter
			if (Parent != null && (Parent.AllowNested != true || Parent.IsIdFilter != true))
			{
				return false;
			}

			try
			{
				// create a blank layer filter group
				LayerGroup lg = new LayerGroup();

				// set its name
				lg.Name = Name;

				// add each layer id for the group
				foreach (ObjectId layId in layIds)
				{
					lg.LayerIds.Add(layId);
				}

				if (Parent == null)
				{
					// add the layer filter group to the collection
					lfCollect.Add(lg);
				}
				else
				{
					// add the layer filter as a nested filter
					Parent.NestedFilters.Add(lg);
				}

				// update the database with the updated tree
				db.LayerFilters = lfTree;

				// update the layer palette to show the
				// layer filter changes
				refreshLayerManager();
			}
			catch (System.Exception)
			{
				return false;
			}
			return true;
		}

		/// <summary>
		/// Delete one filter based on the name provided.  If the Filter
		/// does not exist or cannot be deleted, return null
		/// </summary>
		/// <param name="lfTree"></param>
		/// <param name="lfCollect"></param>
		/// <param name="searchName"></param>
		/// <returns></returns>
		private ResultBuffer DeleteFilter(LayerFilterTree lfTree, LayerFilterCollection lfCollect, string searchName)
		{
			// search for the LayerFilter
			List<LayerFilter> lFilters = SearchOneFilter(lfCollect, Name: searchName, allowDelete: true);

			// if the list of layer filters found is null, no filters to delete, return null
			if (lFilters == null)
			{
				return null;
			}

			// filter can be deleted

			// remove from local copy of the collection
			if (lFilters[0].Parent == null)
			{
				// remove from the root collection when
				// parent is null
				lfCollect.Remove(lFilters[0]);
			}
			else
			{
				// else remove from the parent collection
				// when parent is not null
				lFilters[0].Parent.NestedFilters.Remove(lFilters[0]);
			}

			// write the updated layer filter tree back to the database
			db.LayerFilters = lfTree;

			// update the layer palette to 
			// show the layer filter changes
			refreshLayerManager();

			return BuildResBuffer(lFilters);
		}

		/// <summary>
		/// Deletes all of the filters in the List provided
		/// </summary>
		/// <param name="lfTree"></param>
		/// <param name="lfCollect"></param>
		/// <param name="lFilters"></param>
		/// <returns></returns>
		private ResultBuffer DeleteFilters(LayerFilterTree lfTree, LayerFilterCollection lfCollect, List<LayerFilter> lFilters)
		{
			ResultBuffer resBuffer = new ResultBuffer();

			if (lFilters.Count == 0) { return resBuffer; }

			foreach (LayerFilter lFilter in lFilters)
			{
				DeleteFilter(lfTree, lfCollect, lFilter.Name);
			}

			// return the list of not deleted filters
			return ListFilters(lfCollect);
		}


		/// <summary>
		/// Display the usage message
		/// </summary>
		private void DisplayUsage()
		{
			const string USAGEUSAGE = "(layerFilterUtil \"usage\")";
			const string USAGELIST = "(layerFilterUtil \"list\")";
			const string USAGEFIND = "(layerFilterUtil \"find\" FilterNameToFind)";
			const string USAGEADD = "(layerFilterUtil \"add\" NewFilterList)" + "this is a test";
			const string USAGEDEL = "(layerFilterUtil \"delete\" FilterNameToDelete or \"*\")";

			ed.WriteMessage("Usage:\n" + USAGEUSAGE +
							" or\n" + USAGELIST + " or\n" + USAGEFIND +
							" or\n" + USAGEADD + " or\n" + USAGEDEL + "\n");
		}

		private void AddFilterToResBuffer(LayerFilter lFilter, ResultBuffer ResBuffer)
		{
			// DXF codes for the dotted pairs
			const int FILTERNAMEDXF = 300;
			const int FILTEREXPDXF = 301;
			const int FILTERPARENTDXF = 302;
			const int FILTERLAYERSDXF = 303;

			const int FILTERDELFLGDXF = 290;
			const int FILTERNESTFLGDXF = 291;
			const int FILTERGRPFLGDXF = 292;

			const int FILTERNESTCNTDXF = 90;

			ResBuffer.Add(new TypedValue((int)LispDataType.ListBegin));

			// 
			AddDottedPairToResBuffer(FILTERNAMEDXF, lFilter.Name, ResBuffer);

			// add either the layer expression dotted pair
			// of the list of layers dotted pair
			if (!lFilter.IsIdFilter)
			{
				// add the filter expression to the result buffer
				AddDottedPairToResBuffer(FILTEREXPDXF, lFilter.FilterExpression, ResBuffer);
			}
			else
			{
				// add the list of layers to the result buffer
				StringBuilder sb = new StringBuilder();

				using (Transaction tr = db.TransactionManager.StartTransaction())
				{
					// allocate for LayerTableRecord
					LayerTableRecord ltRecord;

					// iterate through all Layer Id's in the filter
					foreach (ObjectId layId in ((LayerGroup)lFilter).LayerIds)
					{
						// based on the layer Id, the the LayerTableRecord
						ltRecord = tr.GetObject(layId, OpenMode.ForRead) as LayerTableRecord;

						// add the layer name to the list with a trailing '/'
						sb.Append(ltRecord.Name + "/");
					}
				}

				// if the list of found layers is empty, create a "blank" entry
				if (sb.Length == 0) { sb = new StringBuilder("/", 1); }

				// have the formatted list of layers, add the dotted pair
				AddDottedPairToResBuffer(FILTERLAYERSDXF, sb.ToString(), ResBuffer);
			}

			// add dotted pair for the allow delete flag
			AddDottedPairToResBuffer(FILTERDELFLGDXF, lFilter.AllowDelete, ResBuffer);

			// add dotted pair for the parent name
			AddDottedPairToResBuffer(FILTERPARENTDXF,
				lFilter.Parent != null ? lFilter.Parent.Name : "",ResBuffer);

			// add dotted pair for the is id filter flag
			AddDottedPairToResBuffer(FILTERGRPFLGDXF, lFilter.IsIdFilter, ResBuffer);	// true = group filter; false = property filter

			// add dotted pair for the allow nested flag
			AddDottedPairToResBuffer(FILTERNESTFLGDXF, lFilter.AllowNested, ResBuffer);

			// add dotted pair for the nested filter count
			AddDottedPairToResBuffer(FILTERNESTCNTDXF, lFilter.NestedFilters.Count, ResBuffer);

			ResBuffer.Add(new TypedValue((int)LispDataType.ListEnd));
		}

		private ResultBuffer BuildResBuffer(List<LayerFilter> lFilters)
		{
			ResultBuffer resBuffer = new ResultBuffer();

			// if nothing in the filter list, retrun null
			if (lFilters.Count <= 0)
			{
				return null;
			}

			// start the list
			resBuffer.Add(new TypedValue((int)LispDataType.ListBegin));

			// add the item count
			resBuffer.Add(
				new TypedValue((int)LispDataType.Int16, lFilters.Count));

			// begin the list of lists
			resBuffer.Add(new TypedValue((int)LispDataType.ListBegin));

			// add each list item
			foreach (LayerFilter lFilter in lFilters)
			{
				AddFilterToResBuffer(lFilter, resBuffer);
			}

			// end the list of lists
			resBuffer.Add(new TypedValue((int)LispDataType.ListEnd));

			// end the whole list
			resBuffer.Add(new TypedValue((int)LispDataType.ListEnd));

			return resBuffer;
		}

		private ResultBuffer BuildResBuffer(LayerFilter lFilter)
		{
			List<LayerFilter> lFilters = new List<LayerFilter>(1);
			lFilters.Add(lFilter);

			return BuildResBuffer(lFilters);
		}

		void AddDottedPairToResBuffer<T>(int dxfCode, T Value, ResultBuffer ResBuffer)
		{
			// start with a list begin
			ResBuffer.Add(new TypedValue((int)LispDataType.ListBegin));

			// add the DXF code
			ResBuffer.Add(new TypedValue((int)LispDataType.Int16, dxfCode));

			// add the dotted pair value depending on the
			// type of TypedValue
			switch (Type.GetTypeCode(typeof(T)))
			{
				case TypeCode.String:
					ResBuffer.Add(
						new TypedValue((int)LispDataType.Text, Value));
					break;
				case TypeCode.Int16:
					ResBuffer.Add(
						new TypedValue((int)LispDataType.Int16, Value));
					break;
				case TypeCode.Int32:
					ResBuffer.Add(
						new TypedValue((int)LispDataType.Int32, Value));
					break;
				case TypeCode.Empty:
					ResBuffer.Add(
						new TypedValue((int)LispDataType.Text, ""));
					break;
			}

			// terminate the dotted pair
			ResBuffer.Add(new TypedValue((int)LispDataType.DottedPair));
		}

		void AddDottedPairToResBuffer(int dxfCode, bool Value, ResultBuffer ResBuffer)
		{
			// make a standard dotted pair by converting the boolean
			// to a short
			AddDottedPairToResBuffer(dxfCode, (short) (Value ? 1 : 0), ResBuffer);
		}

		private List<LayerFilter> SearchFilters(LayerFilterCollection lfCollect, 
			string Name = null, string Parent = null, 
			bool? allowDelete = null, bool? isGroup = null, 
			bool? allowNested = null, string nestCount = null)
		{
			// create the blank list (no elements - length == 0)
			List<LayerFilter> lfList = new List<LayerFilter>();

			if (lfCollect.Count == 0 || NestDepth > 100) { return lfList; }

			// prevent from getting too deep
			NestDepth++;

			foreach (LayerFilter lFilter in lfCollect)
			{
				if (validateFilter(lFilter, Name, Parent, allowDelete, isGroup, allowNested, nestCount)) {
					lfList.Add(lFilter);
				}

				if (lFilter.NestedFilters.Count != 0)
				{
					List<LayerFilter> lfListReturn = 
						SearchFilters(lFilter.NestedFilters, Name, Parent, allowDelete, isGroup, allowNested, nestCount);

					if (lfListReturn != null && lfListReturn.Count != 0)
					{
						lfList.AddRange(lfListReturn);
					}
				}
			}

			return lfList;

		}

		private List<LayerFilter> SearchOneFilter(LayerFilterCollection lfCollect,
			string Name = null, string Parent = null,
			bool? allowDelete = null, bool? isGroup = null,
			bool? allowNested = null, string nestCount = null)
		{
			// create the list of LayerFilters
			List<LayerFilter> lfList = new List<LayerFilter>(SearchFilters(lfCollect, Name, Parent, allowDelete, isGroup, allowNested, nestCount));

			// return LayerFilter if only 1 found, else return null
			return (lfList.Count == 1 ? lfList : null);
		}

		/// <summary>
		/// Based on the criteria provided, determine if the LayerFilter matches
		/// all criteria fields can be null - a null criteria indicates to not
		/// check that criteria item
		/// </summary>
		/// <param name="lFilter">LayerFilter to check</param>
		/// <param name="Name">Name criteria</param>
		/// <param name="Parent">Parent criteria</param>
		/// <param name="allowDelete">allowDelete flag criteria</param>
		/// <param name="isGroup">is group filter flag criteria</param>
		/// <param name="allowNested">allowNested flag criteria</param>
		/// <param name="nestCount">nestCount (as a string) criteria - 
		/// this requires a comparison operator: ==, !=, <, <=, >, >= </param>
		/// <returns></returns>

		private bool validateFilter(LayerFilter lFilter, 
			string Name = null, string Parent = null, 
			bool? allowDelete = null, bool? isGroup = null, 
			bool? allowNested = null, string nestCount = null) 
		{
			// make easy tests first
			if (Name != null) { if (Name.Equals("") || !Name.Equals(lFilter.Name)) { return false; } }

			if (Parent != null) { if (Parent.Equals("") || !Parent.Equals(lFilter.Parent)) { return false; } }

			if (allowDelete != null) {if (allowDelete == lFilter.AllowDelete) {return false; } }

			if (isGroup != null) { if (isGroup == lFilter.IsIdFilter) { return false; } }

			if (allowNested != null) { if (allowNested == lFilter.AllowNested) { return false; } }

			// process nestCount
			// this allows for a conditional + a number to be
			// specified to determine a match
			if (nestCount != null && nestCount.Length > 1)
			{
				// setup for the nestCount check

				Match m = Regex.Match(nestCount,@"^(=|==|<=|>=|!=|<|>)\s*(\d+)");

				int nestCountValue;

				if (m.Success && int.TryParse(m.Groups[2].Value, out nestCountValue))
				{
					bool nestCountResult = false;

					switch (m.Groups[1].Value)
					{
						case "=":
						case "==":
							nestCountResult = lFilter.NestedFilters.Count == nestCountValue;
							break;
						case "<":
							nestCountResult = lFilter.NestedFilters.Count < nestCountValue;
							break;
						case "<=":
							nestCountResult = lFilter.NestedFilters.Count <= nestCountValue;
							break;
						case ">":
							nestCountResult = lFilter.NestedFilters.Count > nestCountValue;
							break;
						case ">=":
							nestCountResult = lFilter.NestedFilters.Count >= nestCountValue;
							break;
						case "!=":
							nestCountResult = lFilter.NestedFilters.Count != nestCountValue;
							break;
						default:
							nestCountResult = false;
							break;
					}

					if (!nestCountResult) { return false; }
				}
				else
				{
					// regex match failed or int.TryParse failed
					// cannot proceed - return false
					return false;
				}
			}

			// get here, all tests passed
			return true;
		}

		/// <summary>
		/// Created a List from the list of layers provided
		/// </summary>
		/// <param name="tvArgs">The Argument array</param>
		/// <returns></returns>
		private List<string> getLayersFromArg(TypedValue[] tvArgs)
		{
			List<string> layerNames = new List<string>();

			// if there are too few arguments (== no layers), return null
			if (F_LAYERS > tvArgs.Length)
			{
				return layerNames;
			}


			// store the list of layers to add into a sorted list
			for (int i = F_LAYERS; i < tvArgs.Length; i++)
			{
				layerNames.Add(((string)tvArgs[i].Value).ToLower());
			}

			return layerNames;
		}

		/// <summary>
		/// Create a collection of Object Id's (LayerId's)
		/// </summary>
		/// <param name="layerNames">A List of layerNames</param>
		/// <returns>A collection of LayerId's</returns>
		private ObjectIdCollection getLayerIds(List<string> layerNames)
		{
			ObjectIdCollection layIds = new ObjectIdCollection();

			if (layerNames.Count == 0)
			{
				return layIds;
			}

			// process the list of layers and place them into a sorted list
			// since working with a database item, use a transaction
			Transaction tr = db.TransactionManager.StartTransaction();

			using (tr)
			{
				LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

				foreach (ObjectId layId in lt)
				{
					LayerTableRecord ltr = (LayerTableRecord)tr.GetObject(layId, OpenMode.ForRead);

					// check the name of the layer against the list of names to
					// add - if the key is contained, save the layer id and remove the
					// key from the add list
					if (layerNames.IndexOf(ltr.Name.ToLower()) >= 0)
					{
						layIds.Add(layId);
						layerNames.Remove(ltr.Name.ToLower());

						// if all of the layers get removed, done
						if (layerNames.Count == 0)
						{
							break;
						}
					}
				}

				tr.Commit();
			}

			return layIds;

		}

		/*/// <summary>
		/// Scan through the the layer filter collection and find a filter<para />
		/// that matches the name provided - exact match is required
		/// </summary>
		/// <param name="lfC">A Layer Filter Collection</param>
		/// <param name="nameToFind">The name of the Layer Filter to find</param>
		/// <returns></returns>
		private LayerFilter FindOneFilter(LayerFilterCollection lfC, string nameToFind)
		{
			LayerFilter lFilterFound = null;
			

			if (lfC.Count == 0 || NestDepth > 100)
			{
				return null;
			}

			NestDepth++;

			foreach (LayerFilter lFilter in lfC)
			{

				if (lFilter.Name == nameToFind)
				{
					lFilterFound = lFilter;
					break;
				}
				else
				{
					if (lFilter.NestedFilters.Count != 0)
					{
						lFilterFound = FindOneFilter(lFilter.NestedFilters, nameToFind);
						if (lFilterFound != null)
						{
							break;
						}
					}
				}
			}

			NestDepth--;

			return lFilterFound;
		}*/

		/// <summary>
		/// Update the LayerManagerPalette so that the layer filter
		/// changes get displayed 
		/// </summary>
		private void refreshLayerManager()
		{
			object manager = Application.GetSystemVariable("LAYERMANAGERSTATE");

			// force a refresh of the layermanager palette to have
			// the changes show up
			if (manager.ToString().Contains("1"))
			{
				doc.SendStringToExecute("layerpalette ", true, false, false);
			}

		}

		/// <summary>
		/// List the information about the args passed to the command
		/// </summary>
		/// <param name="tvArgs">Array of args passed to the command</param>
		private void displayArgs(TypedValue[] tvArgs)
		{
			for (int i = 0; i < tvArgs.Length; i++)
			{
				ed.WriteMessage("arg#: " + i
					+ " : type: " + " \"" + describeLispDateType(tvArgs[i].TypeCode) + "\" (" + tvArgs[i].TypeCode + ")"
					+ " : value: >" + tvArgs[i].Value + "<");

				if (tvArgs[i].TypeCode == (short)LispDataType.Text)
				{
					ed.WriteMessage(" : length: " + ((string)tvArgs[i].Value).Length);
				}

				ed.WriteMessage("\n");

			}
		}

		/// <summary>
		/// Provide the description for the LispDataType
		/// </summary>
		/// <param name="tv">Short of the LispDataType</param>
		/// <returns></returns>
		private string describeLispDateType(short tv)
		{
			// todo - complete the below list
			switch (tv)
			{
				case (short)LispDataType.DottedPair:
					return "Dotted pair";
					break;
				case (short)LispDataType.Int16:
					return "Int16";
					break;
				case (short)LispDataType.Int32:
					return "Int32";
					break;
				case (short)LispDataType.ListBegin:
					return "ListBegin";
					break;
				case (short)LispDataType.ListEnd:
					return "ListEnd";
					break;
				case (short)LispDataType.None:
					return "None";
					break;
				case (short)LispDataType.Nil:
					return "Nil";
					break;
				case (short)LispDataType.Void:
					return "Dotted pair";
					break;
				case (short)LispDataType.Text:
					return "Text";
					break;
				default:
					return tv.ToString();

			}
		}
	}
}
