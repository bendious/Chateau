#!/usr/bin/python
# Save to 'AppData\Roaming\GIMP\2.10\plug-ins' to install as a menu option for GIMP2.1

from gimpfu import *
import math


def TextureNormalize(image, layerOrChannel, fullImage, scalar):
	# create new layer to copy to for safety
	layer = gimp.Layer(image, "normalized" if fullImage else layerOrChannel.name + "Normalized", image.width if fullImage else layerOrChannel.width, image.height if fullImage else layerOrChannel.height, RGBA_IMAGE)
	layerToSample = layerOrChannel
	layersCached = image.layers
	if fullImage:
		layerToSample = gimp.GroupLayer(image, "normalizeTempGroup")
		image.add_layer(layerToSample, 0) # NOTE that we have to add the temporary layer before we can sample it
		for layerToCopy in layersCached:
			gimp.pdb.gimp_image_insert_layer(image, layerToCopy.copy(), layerToSample, len(layerToSample.children)) # NOTE that we have to copy layers in order to re-insert them into a group; https://gist.github.com/gorenje/4064292be119a466f01f8005a5a7cb49 - https://stackoverflow.com/questions/13882808/gimp-python-fu-nested-group-layers
	else:
		layer.set_offsets(layerOrChannel.offsets[0], layerOrChannel.offsets[1])
	
	for x in range(0, layer.width):
		for y in range(0, layer.height):
			pixelI = layerToSample.get_pixel(x, y)
			
			# convert from [0,255] to [0.0,1.0] and get magnitude
			pixelF = (pixelI[0] / 255.0, pixelI[1] / 255.0, pixelI[2] / 255.0)
			pixelFNeg = (pixelF[0] * 2.0 - 1.0, pixelF[1] * 2.0 - 1.0, pixelF[2] * 2.0 - 1.0)
			pixelMag = math.sqrt(pixelFNeg[0]**2 + pixelFNeg[1]**2 + pixelFNeg[2]**2)
			pixelMagScaled = pixelMag / scalar # NOTE that this effectively multiplies the final result by scalar w/ fewer operations
			
			# normalize and return to [0,255]
			pixelNormFNeg = (pixelFNeg[0] / pixelMagScaled, pixelFNeg[1] / pixelMagScaled, pixelFNeg[2] / pixelMagScaled)
			pixelNormF = (pixelNormFNeg[0] * 0.5 + 0.5, pixelNormFNeg[1] * 0.5 + 0.5, pixelNormFNeg[2] * 0.5 + 0.5)
			pixelNormI = (int(pixelNormF[0] * 255.0), int(pixelNormF[1] * 255.0), int(pixelNormF[2] * 255.0), pixelI[3])
			
			layer.set_pixel(x, y, pixelNormI)
	
	if fullImage:
		image.remove_layer(layerToSample)
	
	# add layer
	image.add_layer(layer, 0)


# add to GIMP menu
register("TextureNormalize",
		"Normalizes the length of each RGB tuple for use as a normal map.",
		"Normalizes the length of each RGB tuple for use as a normal map.",
		"Benjamin Laws",
		"Benjamin Laws",
		"2022",
		"<Image>/Filters/Normal Map Normalize",
		"RGB*",
		[
			(PF_BOOL, "fullImage", "Full Image", False),
			(PF_FLOAT, "scalar", "Scalar", 1.0),
		],
		[],
		TextureNormalize)
main()
