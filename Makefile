all: 
	gmcs -t:library MergeVersions.cs -resource:MergeVersions.addin.xml -pkg:f-spot -pkg:gtk-sharp-2.0  -r:Mono.Posix
clean:
	-rm MergeVersions.dll
