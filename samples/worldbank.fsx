// ----------------------------------------------------------------------------
// Real-World Functional Programming (Chapter 13)
// ----------------------------------------------------------------------------

#r "System.Xml.dll"
#r "System.Xml.Linq.dll"

open System.IO
open System.Net
open System.Web
open System.Xml.Linq

// ----------------------------------------------------------------------------
// PART 1: Downloading data from the World Bank using REST API
// ----------------------------------------------------------------------------

let worldBankDownload req args = async {
  let key = "hq8byg8k7t2fxc6hp7jmbx26"
  let url = sprintf "http://open.worldbank.org/%s?per_page=100&api_key=%s%s" req key args
  let req = HttpWebRequest.Create(url)
  let! resp = req.AsyncGetResponse()
  use response = resp
  let stream = response.GetResponseStream()
  use reader = new StreamReader(stream)
  let! res = reader.ReadToEndAsync() |> Async.AwaitTask
  return XDocument.Parse(res) }

// ----------------------------------------------------------------------------
// Working with data downloaded from the  World Bank interactively

fsi.AddPrinter(fun (x:XContainer) ->
  let s = x.ToString()
  if s.Length < 1000 then s
  else s.Substring(0, 1000) + "..." )

let doc = Async.RunSynchronously(worldBankDownload "countries" "&region=NA")

let xn s = XName.Get(s)
let xnw s = XName.Get(s, "http://www.worldbank.org")

// Look at the first returned country

let c = doc.Element(xnw "countries").Element(xnw "country")
c.Attribute(xn "id").Value
c.Element(xnw "name").Value


let regions = 
  [ for r in doc.Element(xnw "countries").Elements(xnw "country") do
      yield r.Element(xnw "name").Value ]

// ----------------------------------------------------------------------------
// PART 2: Downloading area covered by forests
// ----------------------------------------------------------------------------

let rec getIndicatorData ind date page = async {
  let req = sprintf "countries/indicators/%s" ind 
  let args = sprintf "&date=%s&page=%d" date page
  let! doc = worldBankDownload req args
  let pages = doc.Element(xnw "data").Attribute(xn "pages") |> int
  if (pages = page) then return [doc]
  else 
    let! rest = getIndicatorData ind date (page + 1)
    return doc::rest }

let frst = 
  getIndicatorData "AG.LND.FRST.ZS" "1990:1990" 1
  |> Async.RunSynchronously

let downloadAll = 
  Async.Parallel 
    [ for ind in [ "AG.SRF.TOTL.K2"; "AG.LND.FRST.ZS" ] do 
        for year in [ "1990:1990"; "2000:2000"; "2005:2005" ] do 
           yield getIndicatorData ind year 1 ]
          
let alldata = Async.RunSynchronously(downloadAll)

// ----------------------------------------------------------------------------

let readSingleValue (node:XElement) =
  let value = node.Element(xnw "value").Value
  let country = node.Element(xnw "country").Value
  let date = node.Element(xnw "date").Value |> int
  if (value = "") then [] 
  else [ (date, country), value ]

let readValues (data:list<XDocument>) = 
  [ for page in data do
      for node in page.Element(xnw "data").Elements(xnw "data") do
        yield! node |> readSingleValue ]

// Read values for the first loaded indicator
alldata.[0] |> readValues

// ----------------------------------------------------------------------------
// PART 3: Converting input into structured data
// ----------------------------------------------------------------------------

[<Measure>] type km
[<Measure>] type percent

let areas = 
  List.concat(alldata.[0 .. 2])
    |> readValues 
    |> List.map (fun (k, v) -> k, (float(v) * 1.0<km^2>))
    |> Map.ofSeq 

let forests = 
  List.concat(alldata.[3 .. 5])
    |> readValues
    |> List.map (fun (k, v) -> k, float(v) * 1.0<percent>) 
    |> Map.ofSeq  
  
  
// Calculating total area covered by forests

let calculateForests (area:float<km^2>) (forest:float<percent>) =
   area * forest / 100.0<percent>

// Run the calculation for sample input
calculateForests 1000.0<km^2> 10.0<percent>

// ----------------------------------------------------------------------------
// Create list of data that can be visualized

let years = [| 1990; 2000; 2005 |]

// Check if all data is available for the specified region
let dataAvailable key = 
   years |> Seq.forall (fun y ->
      areas.ContainsKey(y, key) && forests.ContainsKey(y, key) )

// Get data for the specified year as a list of values
let getData y =
  [ for name in regions do      
      if dataAvailable(name) then    
        yield calculateForests areas.[y, name] forests.[y, name] ]

// Collect data for all years                
let stats = 
  [| for y in years -> y, getData y |]

// Get names of regions with available data
let names = regions |> List.filter dataAvailable
  
// ----------------------------------------------------------------------------
// PART 4: Drawing plots 
// ----------------------------------------------------------------------------

#load "../src/gnuplot.fs"
open FSharp.GnuPlot
open System.Drawing

let gp = new GnuPlot()
gp.Set(output = Output(X11, font="arial"))

// Simple examples of working with gnuplot

gp.Plot("sin(x)")
gp.Plot(Series.Lines([3.0; 2.0; 4.0; 1.0;], title="Sample"))

// Plot multiple histograms into a single plot

gp.Plot
  [ for year, values in stats ->
      Series.Histogram(data = (values |> List.map float)) ]


// Set more properties of 'gnuplot'

gp.Set
  ( output = Output(X11, font = "arial"),
    style = Style(Solid), 
    range = RangeY.[ -5e5 .. 6e7],
    titles = Titles(x = names, xrotate = -90) )

let colors = 
  [ Color.OliveDrab; Color.SteelBlue; Color.Goldenrod ]
        
gp.Plot
  [ for (y, values), clr in Seq.zip stats colors ->
      Series.Histogram
        ( data = (values |> List.map float), 
          title = string y, lineColor = clr) ]

